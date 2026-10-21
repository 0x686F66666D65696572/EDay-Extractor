using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;

namespace Index.Profiles.GearsEDay.Unreal
{

  /// <summary>
  ///   Recovers simple property values from the property block that was skipped because no mappings are loaded.
  /// </summary>
  public static class PropertyRecovery
  {

    #region Constants

    private const int MAX_ARRAY_LENGTH = 256;

    #endregion

    #region Public Methods

    /// <summary>
    ///   Finds a TArray of material references (e.g. UGeometryCollection.Materials) in the skipped properties.
    ///   Such an array is serialized as an int32 count followed by that many FPackageIndex values, so this looks
    ///   for a count followed only by references to imported materials, and takes the longest one.
    /// </summary>
    public static FPackageIndex[] FindMaterialArray( UObject obj )
    {
      var data = NoMappingsFallback.GetSkippedPropertyData( obj );
      var owner = obj.Owner;
      if ( data.Length < 8 || owner is null )
        return [];

      var isMaterialCache = new Dictionary<int, bool>();
      bool IsMaterialReference( int value )
      {
        if ( value >= 0 )
          return false;

        if ( !isMaterialCache.TryGetValue( value, out var isMaterial ) )
        {
          var className = new FPackageIndex( owner, value ).ResolvedObject?.Class?.Name.Text;
          isMaterial = className is not null
            && className.StartsWith( "Material", StringComparison.Ordinal )
            && !className.StartsWith( "MaterialFunction", StringComparison.Ordinal );
          isMaterialCache[ value ] = isMaterial;
        }

        return isMaterial;
      }

      var best = Array.Empty<FPackageIndex>();
      for ( var offset = 0; offset + 8 <= data.Length; offset++ )
      {
        var count = BitConverter.ToInt32( data, offset );
        if ( count <= best.Length || count > MAX_ARRAY_LENGTH || offset + 4 + count * 4 > data.Length )
          continue;

        var values = new int[ count ];
        var valid = true;
        for ( var i = 0; i < count && valid; i++ )
        {
          values[ i ] = BitConverter.ToInt32( data, offset + 4 + i * 4 );
          valid = IsMaterialReference( values[ i ] );
        }

        if ( valid )
          best = values.Select( x => new FPackageIndex( owner, x ) ).ToArray();
      }

      return best;
    }

    #endregion

  }

}
