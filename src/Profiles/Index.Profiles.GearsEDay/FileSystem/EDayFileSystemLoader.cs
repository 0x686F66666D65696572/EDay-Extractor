using Index.Domain.FileSystem;

namespace Index.Profiles.GearsEDay.FileSystem
{

  public class EDayFileSystemLoader : FileSystemLoaderBase
  {
    protected override Task OnLoadDevices()
      => LoadDevice( BasePath, path => new EDayVfsDevice( path, ReportStatus, ReportProgress ) );
  }

}
