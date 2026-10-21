using Index.Domain.GameProfiles;
using System.Windows.Media;

namespace Index.App.Models
{

  public class LauncherItem
  {

    public string GameId { get; set; }
    public string GameName { get; set; }
    public string GamePath { get; set; }

    /// <summary>
    ///   True when <see cref="GamePath"/> points at a single archive file rather than a game directory.
    /// </summary>
    public bool IsSourceFile { get; set; }
    public ImageSource? GameIcon { get; set; }
    public IGameProfile GameProfile { get; set; }

  }

}