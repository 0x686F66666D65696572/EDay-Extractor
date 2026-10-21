using System.IO.Enumeration;
using System.Threading.Tasks.Dataflow;

namespace Index.Domain.FileSystem
{

  public abstract class FileSystemLoaderBase : IFileSystemLoader
  {

    #region Constants

    const int THREAD_COUNT = 4;

    #endregion

    #region Events

    public event Action<double> ProgressChanged;
    public event Action<string> StatusChanged;

    #endregion

    #region Data Members

    private string _basePath;
    private List<string>? _sourceFiles;
    private readonly HashSet<string> _deviceFileNames;
    private readonly List<IFileSystemDevice> _loadedDevices;

    private Queue<IFileSystemDevice> _devicesToLoad;

    #endregion

    #region Properties

    protected string BasePath => _basePath;
    protected IReadOnlyList<IFileSystemDevice> Devices => _loadedDevices;

    /// <summary>
    ///   True when the loader was given explicit files via <see cref="SetSourceFiles"/>
    ///   rather than a game directory to scan.
    /// </summary>
    protected bool IsUsingSourceFiles => _sourceFiles is not null;

    #endregion

    #region Constructor

    protected FileSystemLoaderBase()
    {
      _deviceFileNames = new HashSet<string>();
      _loadedDevices = new List<IFileSystemDevice>();

      _devicesToLoad = new Queue<IFileSystemDevice>();
    }

    #endregion

    #region Public Methods

    public void SetBasePath( string basePath )
    {
      ASSERT( !string.IsNullOrWhiteSpace( basePath ), "Invalid path." );
      ASSERT( Directory.Exists( basePath ), "Directory does not exist: {0}", basePath );

      _basePath = basePath;
    }

    public void SetSourceFiles( IEnumerable<string> filePaths )
    {
      ASSERT_NOT_NULL( filePaths );

      var files = filePaths.Select( Path.GetFullPath ).Distinct( StringComparer.OrdinalIgnoreCase ).ToList();
      ASSERT( files.Count > 0, "No source files were specified." );

      foreach ( var file in files )
        ASSERT( File.Exists( file ), "File does not exist: {0}", file );

      _sourceFiles = files;

      // Devices use the base path to build display names, so point it at the common parent directory.
      _basePath = GetCommonDirectory( files );
    }

    public async Task<IReadOnlyList<IFileSystemDevice>> LoadDevices()
    {
      ASSERT( !string.IsNullOrWhiteSpace( _basePath ), "SetBasePath must be called before loading devices." );

      await OnLoadDevices();
      await InitializeDevices();

      return _loadedDevices;
    }

    #endregion

    #region Private Methods

    protected abstract Task OnLoadDevices();

    /// <summary>
    ///   Lets devices report progress (0 to 1) while they initialize, e.g. when indexing a large archive set.
    /// </summary>
    protected void ReportProgress( double progress )
      => ProgressChanged?.Invoke( progress );

    protected void ReportStatus( string status )
      => StatusChanged?.Invoke( status );

    protected async Task AddDevice( IFileSystemDevice device )
    {
      var result = await device.InitializeAsync();

      if ( result.IsSuccessful )
      {
        lock(_loadedDevices)
          _loadedDevices.Add( device );
      }
    }

    protected Task LoadDevice( string path, Func<string, IFileSystemDevice> deviceFactory )
    {
      var device = deviceFactory( path );
      AddDeviceToLoadQueue( device );

      return Task.CompletedTask;
    }

    protected Task LoadFileWithName( string name, Func<string, IFileSystemDevice> deviceFactory )
    {
      foreach ( var filePath in EnumerateCandidateFiles( name ) )
      {
        var fileName = Path.GetFileNameWithoutExtension( filePath ).ToLower();
        LoadDevice( filePath, deviceFactory );
        break;
      }

      return Task.CompletedTask;
    }

    protected Task LoadFilesWithExtension( string extension, Func<string, IFileSystemDevice> deviceFactory, IEnumerable<string> excludedFileNames = null )
    {
      if ( excludedFileNames is null )
        excludedFileNames = Enumerable.Empty<string>();

      // Files the user picked explicitly are always loaded, even if the profile would normally skip them.
      var excludeSet = IsUsingSourceFiles
        ? new HashSet<string>()
        : new HashSet<string>( excludedFileNames.Select( x => x.ToLower() ) );

      extension = SanitizeExtension( extension );

      foreach ( var filePath in EnumerateCandidateFiles( extension ) )
      {
        var fileName = Path.GetFileNameWithoutExtension( filePath ).ToLower();
        if ( excludeSet.Contains( fileName ) )
          continue;

        // Skip files that have already been loaded
        if ( !_deviceFileNames.Add( filePath ) )
          continue;

        LoadDevice( filePath, deviceFactory );
      }

      return Task.CompletedTask;
    }

    private IEnumerable<string> EnumerateCandidateFiles( string searchPattern )
    {
      if ( _sourceFiles is null )
        return Directory.EnumerateFiles( _basePath, searchPattern, SearchOption.AllDirectories );

      return _sourceFiles.Where( x => FileSystemName.MatchesSimpleExpression( searchPattern, Path.GetFileName( x ) ) );
    }

    private static string GetCommonDirectory( IReadOnlyList<string> files )
    {
      var firstDirectory = Path.GetDirectoryName( files[ 0 ] )!;

      string? common = firstDirectory;
      foreach ( var file in files.Skip( 1 ) )
      {
        var directory = Path.GetDirectoryName( file )!;
        while ( common is not null && !IsSameOrChildDirectory( directory, common ) )
          common = Path.GetDirectoryName( common );
      }

      // Files on different drives share no directory; fall back to the first file's directory.
      return common ?? firstDirectory;
    }

    private static bool IsSameOrChildDirectory( string directory, string parent )
    {
      var relative = Path.GetRelativePath( parent, directory );
      var escapesParent = relative == ".." || relative.StartsWith( ".." + Path.DirectorySeparatorChar );
      return !escapesParent && !Path.IsPathRooted( relative );
    }

    private void AddDeviceToLoadQueue( IFileSystemDevice device )
    {
      lock ( _devicesToLoad )
        _devicesToLoad.Enqueue( device );
    }

    private async Task InitializeDevices()
    {
      int loadedDevicesCount = 0;
      int totalDevicesCount = _devicesToLoad.Count;

#if DEBUG
      int threadCount = Environment.ProcessorCount;
#else
      int threadCount = 4;
#endif

      void IncrementLoadProgress()
      {
        var completed = Interlocked.Increment( ref loadedDevicesCount );
        var progress = ( double ) completed / totalDevicesCount;
        ProgressChanged?.Invoke( progress );
      }

      Exception? firstFailure = null;

      var initBlock = new ActionBlock<IFileSystemDevice>( async device =>
      {
        var result = await device.InitializeAsync();
        if ( result.IsSuccessful )
        {
          lock ( _loadedDevices )
            _loadedDevices.Add( device );
        }
        else
        {
          Serilog.Log.Error( result.Exception, "Device failed to initialize: {message}", result.Message );
          Interlocked.CompareExchange( ref firstFailure, result.Exception ?? new Exception( result.Message ), null );
        }

        IncrementLoadProgress();
      }, new() { EnsureOrdered = false, MaxDegreeOfParallelism = threadCount } );


      while ( _devicesToLoad.TryDequeue( out var device ) )
        initBlock.Post( device );

      initBlock.Complete();
      await initBlock.Completion;

      // Nothing loaded at all: report why instead of opening an empty editor.
      if ( _loadedDevices.Count == 0 && firstFailure is not null )
        throw new InvalidOperationException( firstFailure.Message, firstFailure );
    }

    private static string SanitizeExtension( string extension )
    {
      if ( extension.StartsWith( "*." ) )
        return extension;

      if ( extension.StartsWith( "." ) )
        return $"*{extension}";

      return $"*.{extension}";
    }

#endregion

  }

}
