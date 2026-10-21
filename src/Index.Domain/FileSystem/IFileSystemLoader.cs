namespace Index.Domain.FileSystem
{

  public interface IFileSystemLoader
  {

    event Action<double> ProgressChanged;
    event Action<string> StatusChanged;

    void SetBasePath( string basePath );
    void SetSourceFiles( IEnumerable<string> filePaths );
    Task<IReadOnlyList<IFileSystemDevice>> LoadDevices();

  }

}
