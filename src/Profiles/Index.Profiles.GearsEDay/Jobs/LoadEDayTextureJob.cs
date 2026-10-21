using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;
using Index.Domain.Assets;
using Index.Domain.Assets.Textures;
using Index.Domain.Assets.Textures.Dxgi;
using Index.Jobs;
using Index.Profiles.GearsEDay.FileSystem;
using Index.Profiles.GearsEDay.Unreal;
using Index.Textures;
using Prism.Ioc;
using SkiaSharp;

namespace Index.Profiles.GearsEDay.Jobs
{

  /// <summary>
  ///   Decodes a Texture2D (BCn, BC7, uncompressed or virtual texture) to 8-bit BGRA for Index's texture viewer/exporter.
  /// </summary>
  public class LoadEDayTextureJob : JobBase<DxgiTextureAsset>
  {

    #region Data Members

    private readonly IDxgiTextureService _dxgiTextureService;
    private readonly IAssetReference _assetReference;

    #endregion

    #region Constructor

    public LoadEDayTextureJob( IContainerProvider container, IParameterCollection parameters )
      : base( container, parameters )
    {
      _dxgiTextureService = container.Resolve<IDxgiTextureService>();
      _assetReference = Parameters.Get<IAssetReference>();

      Name = $"Loading {_assetReference.AssetName}";
    }

    #endregion

    #region Overrides

    protected override Task OnExecuting()
    {
      return Task.Run( () =>
      {
        SetStatus( "Reading Texture" );
        SetIndeterminate();

        var node = ( EDayFileNode ) _assetReference.Node;
        var texture = EDayAssetLoader.LoadMainExport<UTexture2D>( node.GameFile );

        SetStatus( "Decoding Texture" );
        var decoded = texture.Decode()
          ?? throw new InvalidDataException( $"{node.AssetPath} has no decodable mip data (format {texture.Format})." );

        var (pixels, width, height) = ToBgra8( decoded );
        var info = new DxgiTextureInfo
        {
          Width = width,
          Height = height,
          Depth = 1,
          FaceCount = 1,
          MipCount = 1,
          Format = DxgiTextureFormat.DXGI_FORMAT_B8G8R8A8_UNORM
        };

        var dxgiImage = _dxgiTextureService.CreateDxgiImageFromRawTextureData( pixels, info );
        SetResult( new DxgiTextureAsset( _assetReference, GuessTextureType( node.AssetPath ), dxgiImage ) );
      } );
    }

    #endregion

    #region Private Methods

    private static (byte[] Pixels, int Width, int Height) ToBgra8( CTexture texture )
    {
      using var bitmap = texture.ToSkBitmap();
      using var bgra = bitmap.ColorType == SKColorType.Bgra8888 ? null : bitmap.Copy( SKColorType.Bgra8888 );
      var source = bgra ?? bitmap;

      var rowBytes = source.Width * 4;
      var pixels = new byte[ rowBytes * source.Height ];
      var span = source.GetPixelSpan();
      for ( var y = 0; y < source.Height; y++ )
        span.Slice( y * source.RowBytes, rowBytes ).CopyTo( pixels.AsSpan( y * rowBytes ) );

      return (pixels, source.Width, source.Height);
    }

    internal static TextureType GuessTextureType( string assetPath )
    {
      var name = Path.GetFileName( assetPath );
      if ( EDayAssetLoader.IsNormalMapName( name ) )
        return TextureType.Normals;

      var lower = name.ToLowerInvariant();
      if ( lower.EndsWith( "_e" ) || lower.Contains( "emissive" ) )
        return TextureType.Emission;
      if ( lower.EndsWith( "_orm" ) || lower.EndsWith( "_mra" ) || lower.EndsWith( "_rma" ) || lower.EndsWith( "_m" ) || lower.EndsWith( "_mask" ) )
        return TextureType.ChannelPacked;
      if ( lower.EndsWith( "_r" ) || lower.EndsWith( "_roughness" ) )
        return TextureType.Roughness;
      if ( lower.EndsWith( "_ao" ) )
        return TextureType.AmbientOcclusion;
      if ( EDayAssetLoader.PickTexture( new[] { name }, new[] { "_d", "_bc", "_c", "_basecolor", "_albedo", "_diffuse" } ) is not null )
        return TextureType.Diffuse;

      return TextureType.Unknown;
    }

    #endregion

  }

}
