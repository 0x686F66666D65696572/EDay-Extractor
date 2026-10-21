using Index.Domain.Assets;
using Index.Domain.Assets.Meshes;
using Index.Domain.Assets.Text;
using Index.Domain.Assets.Textures.Dxgi;
using Index.Jobs;
using Index.Profiles.GearsEDay.Jobs;
using Prism.Ioc;

namespace Index.Profiles.GearsEDay.Assets
{

  [AssetExportOptionsType( typeof( MeshAssetExportOptions ) )]
  public class EDayStaticMeshAsset : MeshAsset
  {
    public override string TypeName => "Static Mesh";

    public EDayStaticMeshAsset( IAssetReference assetReference )
      : base( assetReference )
    {
    }
  }

  [AssetExportOptionsType( typeof( MeshAssetExportOptions ) )]
  public class EDaySkeletalMeshAsset : MeshAsset
  {
    public override string TypeName => "Skeletal Mesh";

    public EDaySkeletalMeshAsset( IAssetReference assetReference )
      : base( assetReference )
    {
    }
  }

  /// <summary>
  ///   Chaos destruction assets. E-Day stores several complete weapons only this way (e.g. the Gnasher).
  /// </summary>
  [AssetExportOptionsType( typeof( MeshAssetExportOptions ) )]
  public class EDayGeometryCollectionAsset : MeshAsset
  {
    public override string TypeName => "Geometry Collection";

    public EDayGeometryCollectionAsset( IAssetReference assetReference )
      : base( assetReference )
    {
    }
  }

  public class EDayTextureAssetFactory : AssetFactoryBase<DxgiTextureAsset>
  {
    public EDayTextureAssetFactory( IContainerProvider container )
      : base( container )
    {
    }

    public override IJob<DxgiTextureAsset> LoadAsset( IAssetReference assetReference, IAssetLoadContext assetLoadContext = null )
    {
      var parameters = new ParameterCollection();
      parameters.Set( assetReference );
      parameters.Set( assetLoadContext );

      return JobManager.StartJob<LoadEDayTextureJob>( parameters );
    }
  }

  public class EDayMeshAssetFactory<TAsset> : AssetFactoryBase<TAsset>
    where TAsset : MeshAsset
  {
    public EDayMeshAssetFactory( IContainerProvider container )
      : base( container )
    {
    }

    public override IJob<TAsset> LoadAsset( IAssetReference assetReference, IAssetLoadContext assetLoadContext = null )
    {
      var parameters = new ParameterCollection();
      parameters.Set( assetReference );
      parameters.Set( assetLoadContext );

      return JobManager.StartJob<LoadEDayMeshJob<TAsset>>( parameters );
    }
  }

  public class EDayTextAssetFactory : AssetFactoryBase<TextAsset>
  {
    public EDayTextAssetFactory( IContainerProvider container )
      : base( container )
    {
    }

    public override IJob<TextAsset> LoadAsset( IAssetReference assetReference, IAssetLoadContext assetLoadContext = null )
    {
      var parameters = new ParameterCollection();
      parameters.Set( assetReference );
      parameters.Set( assetLoadContext );

      return JobManager.StartJob<LoadEDayTextJob>( parameters );
    }
  }

}
