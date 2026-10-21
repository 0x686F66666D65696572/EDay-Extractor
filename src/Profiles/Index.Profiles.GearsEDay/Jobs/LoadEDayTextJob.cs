using Index.Domain.Assets;
using Index.Domain.Assets.Text;
using Index.Jobs;
using Prism.Ioc;

namespace Index.Profiles.GearsEDay.Jobs
{

  public class LoadEDayTextJob : JobBase<TextAsset>
  {

    #region Data Members

    private readonly IAssetReference _assetReference;

    #endregion

    #region Constructor

    public LoadEDayTextJob( IContainerProvider container, IParameterCollection parameters )
      : base( container, parameters )
    {
      _assetReference = Parameters.Get<IAssetReference>();
      Name = $"Loading {_assetReference.AssetName}";
    }

    #endregion

    #region Overrides

    protected override Task OnExecuting()
    {
      return Task.Run( () =>
      {
        SetStatus( "Reading File" );
        SetIndeterminate();

        var asset = new TextAsset( _assetReference ) { TextStream = _assetReference.Node.Open() };
        SetResult( asset );
      } );
    }

    #endregion

  }

}
