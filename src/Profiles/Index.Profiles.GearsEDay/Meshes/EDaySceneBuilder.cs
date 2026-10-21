using Assimp;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse_Conversion.Dto;
using Num = System.Numerics;

namespace Index.Profiles.GearsEDay.Meshes
{

  /// <summary>
  ///   A material slot of the mesh, with the texture asset names the viewer/exporter should use.
  /// </summary>
  public record EDayMaterialInfo( string Name, string? DiffuseTexture, string? NormalTexture, IReadOnlyList<string> OtherTextures );

  /// <summary>
  ///   Converts CUE4Parse mesh data into an Assimp scene.
  ///   Unreal is left-handed Z-up; swapping Y and Z gives Assimp's right-handed Y-up (units stay in centimeters).
  /// </summary>
  public class EDaySceneBuilder
  {

    #region Data Members

    private readonly Scene _scene;
    private readonly string _meshName;
    private readonly IReadOnlyList<EDayMaterialInfo> _materials;
    private readonly HashSet<string> _lodMeshNames;
    private readonly Dictionary<int, int> _materialIndexLookup;

    private MeshBoneDto[]? _bones;
    private Num.Matrix4x4[]? _boneGlobalTransforms;

    #endregion

    #region Properties

    public ISet<string> LodMeshNames => _lodMeshNames;

    #endregion

    #region Constructor

    public EDaySceneBuilder( string meshName, IReadOnlyList<EDayMaterialInfo> materials )
    {
      _meshName = meshName;
      _materials = materials;
      _lodMeshNames = new HashSet<string>();
      _materialIndexLookup = new Dictionary<int, int>();

      _scene = new Scene { RootNode = new Node( meshName ) };
    }

    #endregion

    #region Public Methods

    public Scene Build<TVertex>( MeshDto<TVertex> mesh )
      where TVertex : struct, IMeshVertex
    {
      if ( mesh is SkeletonDto skeleton && skeleton.Bones.Length > 0 )
        AddSkeleton( skeleton.Bones );

      // The Nanite mesh (full detail) comes first when the asset has one; the regular LODs follow and are
      // flagged as LODs so "Remove LODs" on export keeps only the best geometry.
      var lodNumber = 0;
      var isFirst = true;
      foreach ( var lod in mesh.LODs )
      {
        var lodName = lod.IsNanite ? $"{_meshName}_Nanite" : $"{_meshName}_LOD{lodNumber}";
        var lodNode = new Node( lodName, _scene.RootNode );
        _scene.RootNode.Children.Add( lodNode );

        for ( var sectionIndex = 0; sectionIndex < lod.Sections.Length; sectionIndex++ )
        {
          var section = lod.Sections[ sectionIndex ];
          if ( !section.IsValid || section.NumFaces <= 0 )
            continue;

          var assimpMesh = BuildSectionMesh( lod, section, $"{lodName}_{sectionIndex}" );
          if ( assimpMesh is null )
            continue;

          if ( !isFirst )
            _lodMeshNames.Add( assimpMesh.Name );

          lodNode.MeshIndices.Add( _scene.Meshes.Count );
          _scene.Meshes.Add( assimpMesh );
        }

        if ( !lod.IsNanite )
          lodNumber++;
        isFirst = false;
      }

      if ( _scene.Materials.Count == 0 )
        _scene.Materials.Add( new Material { Name = "DefaultMaterial" } );

      return _scene;
    }

    #endregion

    #region Private Methods

    private Mesh? BuildSectionMesh<TVertex>( MeshLodDto<TVertex> lod, MeshSectionDto section, string name )
      where TVertex : struct, IMeshVertex
    {
      var mesh = new Mesh( name, PrimitiveType.Triangle )
      {
        MaterialIndex = GetMaterialIndex( section.MaterialIndex )
      };

      var hasColors = lod.VertexColors is { Length: > 0 } && lod.VertexColors[ 0 ].Colors.Length == lod.Vertices.Length;
      var extraUvChannels = Math.Min( lod.ExtraUvs.Length, 7 );
      var remap = new Dictionary<uint, int>();
      var boneWeights = _bones is null ? null : new Dictionary<int, List<VertexWeight>>();

      int AddVertex( uint sourceIndex )
      {
        if ( remap.TryGetValue( sourceIndex, out var index ) )
          return index;

        var vertex = lod.Vertices[ sourceIndex ];
        index = mesh.Vertices.Count;
        remap.Add( sourceIndex, index );

        mesh.Vertices.Add( ToAssimpPosition( vertex.Position ) );
        mesh.Normals.Add( ToAssimpDirection( vertex.Normal ) );
        mesh.Tangents.Add( ToAssimpDirection( vertex.Tangent ) );
        mesh.TextureCoordinateChannels[ 0 ].Add( new Vector3D( vertex.Uv.U, 1 - vertex.Uv.V, 0 ) );
        for ( var channel = 0; channel < extraUvChannels; channel++ )
        {
          var uv = lod.ExtraUvs[ channel ][ sourceIndex ];
          mesh.TextureCoordinateChannels[ channel + 1 ].Add( new Vector3D( uv.U, 1 - uv.V, 0 ) );
        }

        if ( hasColors )
        {
          var color = lod.VertexColors![ 0 ].Colors[ sourceIndex ];
          mesh.VertexColorChannels[ 0 ].Add( new Color4D( color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f ) );
        }

        if ( boneWeights is not null && vertex is SkinnedMeshVertex skinned )
        {
          foreach ( var influence in skinned.Influences )
          {
            if ( influence.Weight <= 0 || influence.Bone >= _bones!.Length )
              continue;

            if ( !boneWeights.TryGetValue( influence.Bone, out var weights ) )
              boneWeights[ influence.Bone ] = weights = new List<VertexWeight>();

            weights.Add( new VertexWeight( index, influence.Weight ) );
          }
        }

        return index;
      }

      var firstIndex = section.FirstIndex;
      for ( var face = 0; face < section.NumFaces; face++ )
      {
        var i = firstIndex + face * 3;
        if ( i + 2 >= lod.Indices.Length )
          break;

        var assimpFace = new Face();
        assimpFace.Indices.Add( AddVertex( lod.Indices[ i ] ) );
        assimpFace.Indices.Add( AddVertex( lod.Indices[ i + 1 ] ) );
        assimpFace.Indices.Add( AddVertex( lod.Indices[ i + 2 ] ) );
        mesh.Faces.Add( assimpFace );
      }

      if ( mesh.VertexCount == 0 )
        return null;

      mesh.UVComponentCount[ 0 ] = 2;
      for ( var channel = 0; channel < extraUvChannels; channel++ )
        mesh.UVComponentCount[ channel + 1 ] = 2;

      if ( boneWeights is not null )
        AddBones( mesh, boneWeights );

      return mesh;
    }

    private int GetMaterialIndex( int sourceMaterialIndex )
    {
      if ( _materialIndexLookup.TryGetValue( sourceMaterialIndex, out var index ) )
        return index;

      var info = sourceMaterialIndex >= 0 && sourceMaterialIndex < _materials.Count
        ? _materials[ sourceMaterialIndex ]
        : new EDayMaterialInfo( $"Material_{sourceMaterialIndex}", null, null, Array.Empty<string>() );

      var material = new Material { Name = info.Name };
      if ( info.DiffuseTexture is not null )
        material.TextureDiffuse = CreateTextureSlot( info.DiffuseTexture, TextureType.Diffuse, 0 );
      if ( info.NormalTexture is not null )
        material.TextureNormal = CreateTextureSlot( info.NormalTexture, TextureType.Normals, 1 );

      var slotIndex = 2;
      foreach ( var texture in info.OtherTextures )
        material.AddMaterialTexture( CreateTextureSlot( texture, TextureType.Unknown, slotIndex++ ) );

      index = _scene.Materials.Count;
      _scene.Materials.Add( material );
      _materialIndexLookup.Add( sourceMaterialIndex, index );
      return index;
    }

    private static TextureSlot CreateTextureSlot( string path, TextureType type, int index )
      => new TextureSlot( path, type, index, TextureMapping.FromUV, 0, 0, TextureOperation.Add,
        TextureWrapMode.Wrap, TextureWrapMode.Wrap, 0 );

    private void AddSkeleton( MeshBoneDto[] bones )
    {
      _bones = bones;
      _boneGlobalTransforms = new Num.Matrix4x4[ bones.Length ];

      var nodes = new Node[ bones.Length ];
      for ( var i = 0; i < bones.Length; i++ )
      {
        var bone = bones[ i ];
        var local = ToConvertedMatrix( bone.Transform );

        var parentIndex = bone.ParentIndex;
        var hasParent = parentIndex >= 0 && parentIndex < i;
        _boneGlobalTransforms[ i ] = hasParent ? local * _boneGlobalTransforms[ parentIndex ] : local;

        var parentNode = hasParent ? nodes[ parentIndex ] : _scene.RootNode;
        var node = new Node( bone.Name, parentNode ) { Transform = ToAssimpMatrix( local ) };
        parentNode.Children.Add( node );
        nodes[ i ] = node;
      }
    }

    private void AddBones( Mesh mesh, Dictionary<int, List<VertexWeight>> boneWeights )
    {
      foreach ( var (boneIndex, weights) in boneWeights.OrderBy( x => x.Key ) )
      {
        Num.Matrix4x4.Invert( _boneGlobalTransforms![ boneIndex ], out var inverseBind );

        var bone = new Bone { Name = _bones![ boneIndex ].Name, OffsetMatrix = ToAssimpMatrix( inverseBind ) };
        bone.VertexWeights.AddRange( weights );
        mesh.Bones.Add( bone );
      }
    }

    private static Vector3D ToAssimpPosition( FVector v )
      => new Vector3D( v.X, v.Z, v.Y );

    private static Vector3D ToAssimpDirection( FVector4 v )
    {
      var result = new Vector3D( v.X, v.Z, v.Y );
      result.Normalize();
      return result;
    }

    /// <summary>
    ///   Builds the bone's local matrix (row-vector convention, as in Unreal and System.Numerics)
    ///   and changes its basis by swapping Y and Z: M' = P * M * P.
    /// </summary>
    private static Num.Matrix4x4 ToConvertedMatrix( FTransform transform )
    {
      var q = transform.Rotation;
      var t = transform.Translation;
      var s = transform.Scale3D;

      var matrix = Num.Matrix4x4.CreateScale( s.X, s.Y, s.Z )
        * Num.Matrix4x4.CreateFromQuaternion( Num.Quaternion.Normalize( new Num.Quaternion( q.X, q.Y, q.Z, q.W ) ) )
        * Num.Matrix4x4.CreateTranslation( t.X, t.Y, t.Z );

      var swap = new Num.Matrix4x4(
        1, 0, 0, 0,
        0, 0, 1, 0,
        0, 1, 0, 0,
        0, 0, 0, 1 );

      return swap * matrix * swap;
    }

    /// <summary>
    ///   Assimp matrices use column vectors, so a row-vector matrix is transposed.
    /// </summary>
    private static Assimp.Matrix4x4 ToAssimpMatrix( Num.Matrix4x4 m )
    {
      var result = new Assimp.Matrix4x4(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44 );
      result.Transpose();
      return result;
    }

    #endregion

  }

}
