using System;
using System.Linq;
using System.Collections.Generic;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Assets.Objects.Unversioned;
using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Objects.UObject;

namespace CUE4Parse.UE4.Assets;

/// <summary>
/// EDay-Extractor addition: reads packages with unversioned properties when no .usmap mappings are loaded.
/// The tagged property block of each export is skipped, and the start of the export's native data
/// (mesh buffers, texture mips, ...) is found by trial-parsing from every candidate offset until a parse
/// consumes the export exactly to its end. Properties are therefore empty on objects loaded this way.
/// </summary>
public static class NoMappingsFallback
{
    /// <summary>Allow loading unversioned packages without mappings.</summary>
    public static bool Enabled { get; set; }

    /// <summary>Upper bound on how many bytes of property data are scanned for the native data start.</summary>
    public static int MaxPropertyBytes { get; set; } = 256 * 1024;

    /// <summary>Time budget for finding the native data start of one export.</summary>
    public static TimeSpan SearchTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Optional diagnostics sink: (export type, export name, tried offsets, found offset or -1, elapsed ms).</summary>
    public static Action<string, string, int, long, long>? OnSearchFinished { get; set; }

    /// <summary>
    /// Extra checks a trial parse must pass besides ending at the export end. Without them a wrong offset can
    /// still "succeed", e.g. a static mesh whose bCooked flag reads as false skips its render data entirely.
    /// <c>Slack</c> is how many bytes a validated parse may stop short of the export end: game-specific trailing
    /// data CUE4Parse doesn't read is left over (4 bytes on E-Day static meshes, hundreds of KB on skeletal meshes).
    /// </summary>
    public static readonly List<(Type Type, Func<UObject, bool> IsValid, long Slack)> Validators =
    [
        (typeof(UStaticMesh), o => ((UStaticMesh) o).RenderData?.LODs is { Length: > 0 } lods && lods[0].PositionVertexBuffer?.NumVertices > 0, 16),
        (typeof(USkeletalMesh), o => o is USkeletalMesh
        {
            ReferenceSkeleton.FinalRefBoneInfo.Length: > 0,
            LODModels.Length: > 0
        } sk && sk.LODModels.Any(l => l is { NumVertices: > 0, Sections.Length: > 0 }), long.MaxValue),
        (typeof(UTexture2D), o => ((UTexture2D) o).PlatformData is { SizeX: > 0, SizeY: > 0 }, 16),
    ];

    /// <summary>
    /// Cheap byte-level checks run before a full trial parse, positioned right after the object's
    /// "has GUID" flag. They only need to reject most wrong offsets; the trial parse decides the rest.
    /// </summary>
    public static readonly List<(Type Type, Func<FAssetArchive, bool> Plausible)> Prefilters =
    [
        (typeof(UStaticMesh), Ar =>
        {
            Ar.Position += 2; // FStripDataFlags
            return Ar.Read<uint>() == 1; // bCooked
        }),
        (typeof(USkeletalMesh), Ar =>
        {
            Ar.Position += 2; // FStripDataFlags
            // FBoxSphereBounds ImportedBounds: Origin, BoxExtent, SphereRadius (doubles)
            for (var i = 0; i < 7; i++)
            {
                var v = Ar.Read<double>();
                if (!double.IsFinite(v) || Math.Abs(v) > 1e7) return false;
                if (i >= 3 && v <= 0.001) return false; // a real mesh has a non-empty box and sphere
            }
            var materialCount = Ar.Read<int>();
            if (materialCount is < 1 or > 512) return false;
            // First FSkeletalMaterial: MaterialInterface (FPackageIndex), MaterialSlotName (FName), bSerializeImportedMaterialSlotName (bool32)
            var material = Ar.Read<int>();
            if (Math.Abs(material) > 1_000_000) return false;
            return IsPlausibleFName(Ar) && Ar.Read<uint>() <= 1;
        }),
    ];

    /// <summary>
    /// Property values substituted for the skipped property block, where native serialization depends on them.
    /// E-Day skeletal meshes self-detect a missing color buffer, so assuming vertex colors is the safe choice.
    /// </summary>
    public static readonly List<(Type Type, Func<FPropertyTag[]> Properties)> DefaultProperties =
    [
        (typeof(USkeletalMesh), () => [new FPropertyTag { Name = new FName("bHasVertexColors"), Tag = new BoolProperty(true) }]),
    ];

    /// <summary>Diagnostics: called for the trial that read furthest when no offset was accepted (start, reached, error).</summary>
    public static Action<long, long, string>? OnBestFailure { get; set; }

    [ThreadStatic] private static long _bestReach;
    [ThreadStatic] private static long _bestStart;
    [ThreadStatic] private static string? _bestError;

    [ThreadStatic] private static bool _hasForcedStart;
    [ThreadStatic] private static long _forcedStart;
    [ThreadStatic] private static int _trialDepth;

    /// <summary>True while a trial parse is running; readers use it to reject absurd array lengths early.</summary>
    public static bool InTrial => _trialDepth > 0;

    internal static bool ShouldSkip(FAssetArchive Ar, UStruct struc)
    {
        if (!Enabled || Ar.Owner is null) return false;
        var mappings = Ar.Owner.Mappings;
        if (mappings is null) return true;
        return struc is UScriptClass && !mappings.Types.ContainsKey(struc.Name);
    }

    /// <summary>
    /// Moves <paramref name="Ar"/> past the property block of <paramref name="obj"/>.
    /// Returns false when the native data start could not be determined; the archive is then at <paramref name="validPos"/>.
    /// </summary>
    internal static bool SkipProperties(UObject obj, FAssetArchive Ar, long validPos)
    {
        var type = obj.GetType();
        foreach (var (t, properties) in DefaultProperties)
            if (t.IsAssignableFrom(type))
                obj.Properties.AddRange(properties());

        if (_hasForcedStart)
        {
            _hasForcedStart = false;
            Ar.Position = _forcedStart;
            return true;
        }

        var header = new FUnversionedHeader(Ar);
        if (!header.HasNonZeroValues)
            return true; // nothing serialized, native data starts right after the header

        var dataStart = Ar.Position;
        if (type == typeof(UObject))
        {
            // No native serialization to anchor on; everything left belongs to the properties.
            Ar.Position = validPos;
            return false;
        }

        var prefilters = new List<Func<FAssetArchive, bool>>();
        foreach (var (t, plausible) in Prefilters)
            if (t.IsAssignableFrom(type))
                prefilters.Add(plausible);
        var peekAr = (FAssetArchive) Ar.Clone();

        var end = Math.Min(validPos, dataStart + MaxPropertyBytes);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tried = 0;
        _bestReach = -1; _bestError = null;
        for (var p = dataStart; p < end; p++)
        {
            if (!IsPlausibleStart(peekAr, p, validPos, prefilters)) continue;
            tried++;
            if (TryNativeParse(obj, type, Ar, p, validPos))
            {
                OnSearchFinished?.Invoke(type.Name, obj.Name, tried, p - dataStart, sw.ElapsedMilliseconds);
                Ar.Position = p;
                return true;
            }
            if (sw.Elapsed > SearchTimeout) break;
        }

        OnSearchFinished?.Invoke(type.Name, obj.Name, tried, -1, sw.ElapsedMilliseconds);
        if (_bestError != null) OnBestFailure?.Invoke(_bestStart - dataStart, _bestReach - dataStart, _bestError);
        Ar.Position = validPos;
        return false;
    }

    private static string Describe(UObject o) => o switch
    {
        USkeletalMesh sk => $"bones={sk.ReferenceSkeleton?.FinalRefBoneInfo?.Length} lods={sk.LODModels?.Length} " +
                            string.Join(",", (sk.LODModels ?? []).Select(l => $"[v={l?.NumVertices} s={l?.Sections?.Length}]")),
        UStaticMesh sm => $"lods={sm.RenderData?.LODs?.Length} v={sm.RenderData?.LODs?.FirstOrDefault()?.PositionVertexBuffer?.NumVertices}",
        UTexture2D t => $"{t.PlatformData?.SizeX}x{t.PlatformData?.SizeY}",
        _ => ""
    };

    private static bool IsPlausibleFName(FAssetArchive Ar)
    {
        var index = Ar.Read<uint>();
        var number = Ar.Read<uint>();
        var nameCount = (Ar.Owner as AbstractUePackage)?.NameMap.Length ?? int.MaxValue;
        return index < nameCount && number < 100_000;
    }

    private static bool IsPlausibleStart(FAssetArchive peekAr, long p, long validPos, List<Func<FAssetArchive, bool>> prefilters)
    {
        try
        {
            peekAr.Position = p;
            // UObject::Serialize writes a bool32 "has GUID" right after the properties (then the GUID itself if set)
            var hasGuid = peekAr.Read<uint>();
            if (hasGuid > 1) return false;
            if (hasGuid == 1) peekAr.Position += 16;
            if (peekAr.Position > validPos) return false;

            var afterGuid = peekAr.Position;
            foreach (var plausible in prefilters)
            {
                peekAr.Position = afterGuid;
                if (!plausible(peekAr)) return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Diagnostics: called for every rejected trial (start, reached, error), positions relative to the archive.</summary>
    public static Action<long, long, string>? OnTrialFailed { get; set; }

    private static void Track(long start, long reached, string error)
    {
        OnTrialFailed?.Invoke(start, reached, error);
        if (OnBestFailure is null || reached <= _bestReach) return;
        _bestReach = reached; _bestStart = start; _bestError = error;
    }

    private static bool TryNativeParse(UObject obj, Type type, FAssetArchive Ar, long start, long validPos)
    {
        UObject trial;
        try
        {
            trial = (UObject) Activator.CreateInstance(type)!;
        }
        catch
        {
            return false;
        }

        trial.Name = obj.Name;
        trial.Class = obj.Class;
        trial.Outer = obj.Outer;
        trial.Super = obj.Super;
        trial.Template = obj.Template;
        trial.Flags = obj.Flags;

        var trialAr = (FAssetArchive) Ar.Clone();
        trialAr.Position = start;
        _hasForcedStart = true;
        _forcedStart = start;
        _trialDepth++;
        try
        {
            trial.Deserialize(trialAr, validPos);

            // Slack is only allowed when a validator confirmed the parse produced real data.
            long slack = 0;
            foreach (var (t, isValid, typeSlack) in Validators)
            {
                if (!t.IsAssignableFrom(type)) continue;
                if (!isValid(trial))
                {
                    Track(start, trialAr.Position, "validator rejected " + Describe(trial));
                    return false;
                }
                slack = Math.Max(slack, typeSlack);
            }

            if (trialAr.Position > validPos || validPos - trialAr.Position > slack)
            {
                Track(start, trialAr.Position, $"ended at {trialAr.Position} instead of {validPos}");
                return false;
            }
            return true;
        }
        catch (Exception e)
        {
            Track(start, trialAr.Position, e.GetType().Name + ": " + e.Message + " @ " + string.Join(" | ", (e.StackTrace ?? "").Split(Environment.NewLine).Take(12).Select(l => l.Trim())));
            return false;
        }
        finally
        {
            _trialDepth--;
            _hasForcedStart = false;
        }
    }
}
