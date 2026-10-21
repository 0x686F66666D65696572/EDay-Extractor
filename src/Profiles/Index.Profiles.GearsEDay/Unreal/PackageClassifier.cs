using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Readers;
using Serilog;

namespace Index.Profiles.GearsEDay.Unreal
{

  /// <summary>
  ///   Works out the class of every package's main asset (Texture2D, StaticMesh, ...) by reading only the
  ///   package header, so the asset tree can be built without loading 500k+ packages. Results are cached per install.
  /// </summary>
  public static class PackageClassifier
  {

    #region Constants

    private const int HEADER_READ_SIZE = 64 * 1024;
    private const int CACHE_VERSION = 1;
    public const string UNKNOWN_CLASS = "";

    #endregion

    #region Public Methods

    public static IReadOnlyDictionary<string, string> Classify(
      DefaultFileProvider provider,
      string paksDirectory,
      Action<double>? reportProgress = null,
      CancellationToken cancellationToken = default )
    {
      var cachePath = GetCachePath( paksDirectory );
      if ( TryLoadCache( cachePath, out var cached ) )
      {
        Log.Information( "Loaded {count} package classes from cache {path}", cached.Count, cachePath );
        return cached;
      }

      var packages = provider.Files.Values
        .OfType<FIoStoreEntry>()
        .Where( x => x.IsUePackage && x.Extension.Equals( "uasset", StringComparison.OrdinalIgnoreCase ) )
        .ToList();

      var results = new ConcurrentDictionary<string, string>( StringComparer.OrdinalIgnoreCase );
      var completed = 0;
      var options = new ParallelOptions
      {
        MaxDegreeOfParallelism = Environment.ProcessorCount,
        CancellationToken = cancellationToken
      };

      Parallel.ForEach( packages, options, entry =>
      {
        results[ entry.Path ] = ClassifyPackage( provider, entry );

        var done = Interlocked.Increment( ref completed );
        if ( done % 2000 == 0 )
          reportProgress?.Invoke( ( double ) done / packages.Count );
      } );

      reportProgress?.Invoke( 1 );
      TrySaveCache( cachePath, results );
      return results;
    }

    #endregion

    #region Private Methods

    private static string ClassifyPackage( DefaultFileProvider provider, FIoStoreEntry entry )
    {
      try
      {
        IoPackage package;
        try
        {
          package = ReadPackageHeader( provider, entry, ReadHeaderBytes( entry ) );
        }
        catch
        {
          // Header larger than the first read; fall back to the whole package.
          package = ReadPackageHeader( provider, entry, entry.Read() );
        }

        var exportIndex = package.GetExportIndex( entry.NameWithoutExtension, StringComparison.OrdinalIgnoreCase );
        if ( exportIndex < 0 )
          return UNKNOWN_CLASS;

        return package.ResolveObjectIndex( package.ExportMap[ exportIndex ].ClassIndex )?.Name.Text ?? UNKNOWN_CLASS;
      }
      catch ( Exception ex )
      {
        Log.Debug( ex, "Failed to classify {path}", entry.Path );
        return UNKNOWN_CLASS;
      }
    }

    private static byte[] ReadHeaderBytes( FIoStoreEntry entry )
    {
      var size = ( uint ) Math.Min( entry.Size, HEADER_READ_SIZE );
      if ( size == entry.Size )
        return entry.Read();

      return entry.Read( new FByteBulkDataHeader( 0, ( int ) size, size, 0, default ) );
    }

    private static IoPackage ReadPackageHeader( DefaultFileProvider provider, FIoStoreEntry entry, byte[] bytes )
    {
      var archive = new FByteArchive( entry.Path, bytes, provider.Versions );
      return new IoPackage( archive, entry.IoStoreReader.ContainerHeader, ( FArchive? ) null, ( FArchive? ) null, provider );
    }

    /// <summary>
    ///   The cache is keyed on the size and timestamp of every container, so a game update invalidates it.
    /// </summary>
    private static string GetCachePath( string paksDirectory )
    {
      var signature = new StringBuilder( $"v{CACHE_VERSION}|{paksDirectory.ToLowerInvariant()}" );
      foreach ( var file in Directory.EnumerateFiles( paksDirectory ).OrderBy( x => x, StringComparer.OrdinalIgnoreCase ) )
      {
        var info = new FileInfo( file );
        signature.Append( $"|{info.Name}:{info.Length}:{info.LastWriteTimeUtc.Ticks}" );
      }

      var hash = Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( signature.ToString() ) ) )[ ..16 ];
      return Path.Combine( EDayProviderHost.DataDirectory, $"classes_{hash}.bin" );
    }

    private static bool TryLoadCache( string cachePath, out Dictionary<string, string> classes )
    {
      classes = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
      if ( !File.Exists( cachePath ) )
        return false;

      try
      {
        using var file = File.OpenRead( cachePath );
        using var gzip = new GZipStream( file, CompressionMode.Decompress );
        using var reader = new BinaryReader( gzip, Encoding.UTF8 );

        if ( reader.ReadInt32() != CACHE_VERSION )
          return false;

        var classNames = new string[ reader.ReadInt32() ];
        for ( var i = 0; i < classNames.Length; i++ )
          classNames[ i ] = reader.ReadString();

        var count = reader.ReadInt32();
        classes.EnsureCapacity( count );
        for ( var i = 0; i < count; i++ )
        {
          var path = reader.ReadString();
          classes[ path ] = classNames[ reader.ReadInt32() ];
        }

        return true;
      }
      catch ( Exception ex )
      {
        Log.Warning( ex, "Ignoring unreadable package class cache {path}", cachePath );
        return false;
      }
    }

    private static void TrySaveCache( string cachePath, IReadOnlyDictionary<string, string> classes )
    {
      try
      {
        var classNames = classes.Values.Distinct().ToList();
        var classIndices = classNames.Select( ( name, index ) => (name, index) ).ToDictionary( x => x.name, x => x.index );

        var tempPath = cachePath + ".tmp";
        using ( var file = File.Create( tempPath ) )
        using ( var gzip = new GZipStream( file, CompressionLevel.Fastest ) )
        using ( var writer = new BinaryWriter( gzip, Encoding.UTF8 ) )
        {
          writer.Write( CACHE_VERSION );
          writer.Write( classNames.Count );
          foreach ( var name in classNames )
            writer.Write( name );

          writer.Write( classes.Count );
          foreach ( var (path, className) in classes )
          {
            writer.Write( path );
            writer.Write( classIndices[ className ] );
          }
        }

        File.Move( tempPath, cachePath, overwrite: true );
      }
      catch ( Exception ex )
      {
        Log.Warning( ex, "Failed to save package class cache {path}", cachePath );
      }
    }

    #endregion

  }

}
