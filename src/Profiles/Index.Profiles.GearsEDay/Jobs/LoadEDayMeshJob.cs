using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Options;
using Index.Domain.Assets;
using Index.Domain.Assets.Meshes;
using Index.Domain.Assets.Textures;
using Index.Domain.Assets.Textures.Dxgi;
using Index.Jobs;
using Index.Profiles.GearsEDay.FileSystem;
using Index.Profiles.GearsEDay.Meshes;
using Index.Profiles.GearsEDay.Unreal;
using Prism.Ioc;
using Serilog;

namespace Index.Profiles.GearsEDay.Jobs
{

  /// <summary>
  ///   Loads a StaticMesh or SkeletalMesh with all LODs, its skeleton and the textures its materials use.
  /// </summary>
  public class LoadEDayMeshJob<TAsset> : JobBase<TAsset>
    where TAsset : MeshAsset
  {

    #region Data Members

    private readonly IAssetManager _assetManager;
    private readonly IAssetReference _assetReference;

    #endregion

    #region Constructor

    public LoadEDayMeshJob( IContainerProvider container, IParameterCollection parameters )
      : base( container, parameters )
    {
      _assetManager = container.Resolve<IAssetManager>();
      _assetReference = Parameters.Get<IAssetReference>();

      Name = $"Loading {_assetReference.AssetName}";
    }

    #endregion

    #region Overrides

    protected override async Task OnExecuting()
    {
      SetStatus( "Reading Mesh" );
      SetIndeterminate();

      var node = ( EDayFileNode ) _assetReference.Node;
      var meshObject = await Task.Run( () => EDayAssetLoader.LoadMainExport<UObject>( node.GameFile ) );
      var meshName = node.GameFile.NameWithoutExtension;

      SetStatus( "Converting Mesh" );
      var (scene, lodMeshNames, materials) = await Task.Run( () => BuildScene( meshObject, meshName ) );

      SetStatus( "Loading Textures" );
      var textures = await LoadTextures( materials );

      var asset = ( TAsset ) Activator.CreateInstance( typeof( TAsset ), _assetReference )!;
      asset.AssimpScene = scene;
      asset.Textures = textures;
      asset.LodMeshNames = lodMeshNames;
      SetResult( asset );
    }

    #endregion

    #region Private Methods

    private static (Assimp.Scene, ISet<string>, IReadOnlyList<EDayMaterialInfo>) BuildScene( UObject meshObject, string meshName )
    {
      switch ( meshObject )
      {
        case UStaticMesh staticMesh:
        {
          using var dto = new StaticMeshDto( staticMesh, EMeshQuality.All, ENaniteMeshFormat.NaniteFirst );
          return Build( dto, meshName );
        }

        case USkeletalMesh skeletalMesh:
        {
          using var dto = new SkeletalMeshDto( skeletalMesh, EMeshQuality.All, ENaniteMeshFormat.NaniteFirst, exportMorphTarget: false );
          return Build( dto, meshName );
        }

        default:
          throw new NotSupportedException( $"{meshObject.ExportType} is not a supported mesh type." );
      }
    }

    private static (Assimp.Scene, ISet<string>, IReadOnlyList<EDayMaterialInfo>) Build<TVertex>( MeshDto<TVertex> dto, string meshName )
      where TVertex : struct, IMeshVertex
    {
      if ( dto.LODs.Count == 0 )
        throw new InvalidDataException( $"{meshName} has no LOD with geometry in this install." );

      var materials = dto.Materials.Select( ResolveMaterial ).ToList();
      var builder = new EDaySceneBuilder( meshName, materials );
      var scene = builder.Build( dto );

      return (scene, builder.LodMeshNames, materials);
    }

    private static EDayMaterialInfo ResolveMaterial( MeshMaterialDto material, int index )
    {
      var name = string.IsNullOrEmpty( material.SlotName ) || material.SlotName == "None" ? $"Material_{index}" : material.SlotName;
      var textures = EDayAssetLoader.GetMaterialTexturePaths( material.Material );

      var diffuse = EDayAssetLoader.PickDiffuseTexture( textures );
      var normal = EDayAssetLoader.PickNormalTexture( textures );
      var others = textures.Where( x => x != diffuse && x != normal ).ToList();

      return new EDayMaterialInfo( name, diffuse, normal, others );
    }

    private async Task<IReadOnlyDictionary<string, ITextureAsset>> LoadTextures( IReadOnlyList<EDayMaterialInfo> materials )
    {
      var names = materials
        .SelectMany( x => new[] { x.DiffuseTexture, x.NormalTexture }.Concat( x.OtherTextures ) )
        .Where( x => x is not null )
        .Select( x => x! )
        .Distinct()
        .ToList();

      var textures = new Dictionary<string, ITextureAsset>();
      SetTotalUnits( names.Count );

      foreach ( var name in names )
      {
        SetSubStatus( name );
        try
        {
          if ( !_assetManager.TryGetAssetReference( typeof( DxgiTextureAsset ), name, out var textureReference ) )
            continue;

          var texture = await _assetManager.LoadAssetAsync<DxgiTextureAsset>( textureReference );
          textures[ name ] = texture;
        }
        catch ( Exception ex )
        {
          Log.Warning( ex, "Failed to load texture {texture}", name );
        }
        finally
        {
          IncreaseCompletedUnits( 1 );
        }
      }

      return textures;
    }

    #endregion

  }

}
