using Index.Domain.FileSystem;
using Index.Domain.Models;

namespace Index.Domain.GameProfiles
{

  public interface IGameProfile
  {

    #region Properties

    public string GameId { get; }
    public string GameName { get; }

    public string Author { get; }
    public Version Version { get; }

    public IFileSystemLoader FileSystemLoader { get; }
    public IGamePathIdentificationRule IdentificationRule { get; }

    /// <summary>
    ///   Archive extensions (e.g. ".pak") this profile can open directly, without a game path.
    /// </summary>
    public IReadOnlyList<string> SupportedFileExtensions { get; }

    #endregion

    #region Public Methods

    Stream? LoadGameIcon();
    Task Initialize( IEditorEnvironment environment );

    #endregion

  }

}
