using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Windows.Data;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.SharpDX.Core.Assimp;
using HelixToolkit.SharpDX.Core.Model;
using HelixToolkit.SharpDX.Core.Model.Scene;
using HelixToolkit.Wpf.SharpDX;
using Index.Domain.Assets.Meshes;
using Index.Domain.Assets.Textures;
using Index.Domain.Assets.Textures.Dxgi;
using Index.Textures;
using Index.UI.ViewModels;
using Index.Utilities;
using Prism.Ioc;
using PropertyChanged;
using Serilog;

namespace Index.Modules.MeshEditor.ViewModels
{

  public class SceneViewModel : ViewModelBase
  {

    #region Data Members

    private readonly IDxgiTextureService _dxgiTextureService;

    private readonly object _collectionLock = new object();
    private ObservableCollection<ModelNodeViewModel> _nodes;

    private ActionDebouncer _searchDebouncer;
    private bool _isSettingAllMeshesVisible;

    #endregion

    #region Properties

    public SceneNodeGroupModel3D GroupModel { get; private set; }
    public ICollectionView Nodes { get; private set; }

    [OnChangedMethod( nameof( OnSearchTermChanged ) )]
    public string SearchTerm { get; set; }
    [OnChangedMethod( nameof( OnShowTexturesChanged ) )]
    public bool ShowTextures { get; set; }
    [OnChangedMethod( nameof( OnShowWireframeChanged ) )]
    public bool ShowWireframe { get; set; }

    /// <summary>
    ///   The "Select all" checkbox over the meshes the search currently shows: true when all of them are ticked,
    ///   false when none are, null when some are. Ticking/unticking it applies to all of those meshes, and since
    ///   unticked meshes are left out of exports, it also picks what gets exported.
    /// </summary>
    [DoNotNotify]
    public bool? AllMeshesVisible
    {
      get => GetAllMeshesVisible();
      set => SetAllMeshesVisible( value );
    }

    #endregion

    #region Constructor

    public SceneViewModel( IContainerProvider container )
    {
      _dxgiTextureService = container.Resolve<IDxgiTextureService>();

      _searchDebouncer = new ActionDebouncer( 1000, ApplySearchTerm );
      GroupModel = new SceneNodeGroupModel3D();

      ApplyTransforms();

      ShowTextures = true;
    }

    #endregion

    #region Public Methods

    public void ApplyMeshAsset( IMeshAsset meshAsset )
    {
      var loadTexturesTask = LoadTexturePreviews( meshAsset );

      using ( var importer = new Importer() )
      {
        var nodes = new ObservableCollection<ModelNodeViewModel>();
        importer.ToHelixToolkitScene( meshAsset.AssimpScene, out var helixScene );

        foreach ( var node in helixScene.Root.Traverse() )
        {
          if ( !( node is MeshNode meshNode ) )
            continue;

          meshNode.CullMode = SharpDX.Direct3D11.CullMode.Back;

          var nodeViewModel = new ModelNodeViewModel( meshNode );
          nodeViewModel.PropertyChanged += OnNodePropertyChanged;
          if ( meshAsset.LodMeshNames.Contains( meshNode.Name ) )
          {
            nodeViewModel.IsVisible = false;
            nodeViewModel.IsLod = true;
          }
          else if ( meshAsset.VolumeMeshNames.Contains( meshNode.Name ) )
          {
            nodeViewModel.IsVisible = false;
            nodeViewModel.IsVolume = true;
          }

          nodes.Add( nodeViewModel );
        }

        GroupModel.Dispatcher.BeginInvoke( () =>
        {
          GroupModel.AddNode( helixScene.Root );
        } );

        loadTexturesTask.Wait();
        foreach ( var node in helixScene.Root.Traverse() )
        {
          if ( !( node is MeshNode meshNode ) )
            continue;

          ApplyMaterialToNode( meshAsset, meshNode );
        }

        InitializeNodeCollection( nodes );

        GroupModel.Dispatcher.BeginInvoke( () =>
        {
          GroupModel.SceneNode.ForceUpdateTransformsAndBounds();
          GroupModel.GroupNode.ForceUpdateTransformsAndBounds();
          GroupModel.SceneNode.InvalidateRender();
        } );
      }
    }

    /// <summary>
    ///   Names of the meshes currently hidden in the viewer. A name shared by a visible mesh is left out,
    ///   so a visible mesh is never dropped from an export because a hidden one has the same name.
    /// </summary>
    public ISet<string> GetHiddenMeshNames()
    {
      var hidden = new HashSet<string>();
      var visible = new HashSet<string>();

      lock ( _collectionLock )
      {
        if ( _nodes is null )
          return hidden;

        foreach ( var node in _nodes )
          ( node.IsVisible ? visible : hidden ).Add( node.Name );
      }

      hidden.ExceptWith( visible );
      return hidden;
    }

    public void ForceUpdateBounds()
    {
      GroupModel.InvalidateRender();
      foreach ( var node in GroupModel.SceneNode.Traverse() )
      {
        if ( node is GeometryNode geoNode )
          geoNode.Geometry.UpdateBounds();
      }
    }

    #endregion

    #region Private Methods

    private void ApplyTransforms()
    {
      var transformGroup = new System.Windows.Media.Media3D.Transform3DGroup();

      var rotTransform = new System.Windows.Media.Media3D.RotateTransform3D();
      //rotTransform.Rotation = new System.Windows.Media.Media3D.AxisAngleRotation3D(
      //  new System.Windows.Media.Media3D.Vector3D( 0, 1, 0 ), -90 );
      transformGroup.Children.Add( rotTransform );

      GroupModel.Transform = transformGroup;
    }

    private Task LoadTexturePreviews( IMeshAsset meshAsset )
    {
      var loadBlock = new ActionBlock<ITextureAsset>( LoadTexturePreview,
        new() { MaxDegreeOfParallelism = Environment.ProcessorCount } );

      foreach ( var texture in meshAsset.Textures.Values )
        loadBlock.Post( texture );

      loadBlock.Complete();
      return loadBlock.Completion;
    }

    private void LoadTexturePreview( ITextureAsset asset )
    {
      if ( asset.Images is not null )
        return;

      switch ( asset )
      {
        case IDxgiTextureAsset dxgiTextureAsset:
          GenerateDxgiPreviews( dxgiTextureAsset );
          break;

        default:
          throw new NotImplementedException( $"{asset.GetType().Name} does not support previews." );
      }
    }

    private void GenerateDxgiPreviews( IDxgiTextureAsset asset )
    {
      var previewStreams = _dxgiTextureService.CreateJpegImageStreams( asset.DxgiImage, includeMips: false );

      var images = new List<ITextureAssetImage>();
      for ( var i = 0; i < previewStreams.Length; i++ )
      {
        var previewStream = previewStreams[ i ];
        var image = new TextureAssetImage( i, previewStream );
        images.Add( image );
      }

      asset.SetPreviewImages( images );
    }

    private void ApplyMaterialToNode( IMeshAsset meshAsset, MeshNode meshNode )
    {
      var nodeMaterial = meshNode.Material as PhongMaterialCore;
      if ( nodeMaterial is null )
        return;

      if ( nodeMaterial.DiffuseMap is null && nodeMaterial.DiffuseMapFilePath is not null )
      {
        if ( !meshAsset.Textures.TryGetValue( nodeMaterial.DiffuseMapFilePath, out var texture )
          || texture?.Images is null )
        {
          Log.Logger.Error( "Failed to load texture {texName}.", nodeMaterial.Name );
          return;
        }

        nodeMaterial.DiffuseMap = TextureModel.Create( texture.Images[ 0 ].PreviewStream );
        nodeMaterial.UVTransform = new UVTransform( 0, 1, -1, 0, 0 );
      }
    }

    private void InitializeNodeCollection( ObservableCollection<ModelNodeViewModel> nodes )
    {
      Dispatcher.Invoke( () =>
      {
        _nodes = nodes;
        BindingOperations.EnableCollectionSynchronization( _nodes, _collectionLock );

        var collectionView = CollectionViewSource.GetDefaultView( _nodes );
        collectionView.SortDescriptions.Add( new SortDescription( nameof( ModelNodeViewModel.Name ), ListSortDirection.Ascending ) );
        collectionView.Filter = ( obj ) =>
        {
          if ( string.IsNullOrEmpty( SearchTerm ) )
            return true;

          var node = obj as ModelNodeViewModel;
          return node.Name.Contains( SearchTerm, System.StringComparison.OrdinalIgnoreCase );
        };

        Nodes = collectionView;
        OnPropertyChanged( nameof( AllMeshesVisible ) );
      } );
    }

    private bool? GetAllMeshesVisible()
    {
      if ( Nodes is null )
        return false;

      bool anyVisible = false, anyHidden = false;
      foreach ( ModelNodeViewModel node in Nodes )
      {
        if ( node.IsVisible )
          anyVisible = true;
        else
          anyHidden = true;

        if ( anyVisible && anyHidden )
          return null;
      }

      return anyVisible;
    }

    private void SetAllMeshesVisible( bool? value )
    {
      if ( Nodes is null )
        return;

      // Clicking a partly ticked box selects everything (WPF would otherwise untick it).
      var visible = GetAllMeshesVisible() is null || value == true;

      _isSettingAllMeshesVisible = true;
      try
      {
        foreach ( ModelNodeViewModel node in Nodes )
          node.IsVisible = visible;
      }
      finally
      {
        _isSettingAllMeshesVisible = false;
      }

      OnPropertyChanged( nameof( AllMeshesVisible ) );
    }

    private void OnNodePropertyChanged( object sender, PropertyChangedEventArgs e )
    {
      if ( e.PropertyName == nameof( ModelNodeViewModel.IsVisible ) && !_isSettingAllMeshesVisible )
        OnPropertyChanged( nameof( AllMeshesVisible ) );
    }

    private void OnShowTexturesChanged()
    {
      if ( _nodes is null )
        return;

      foreach ( var node in _nodes )
        node.ShowTexture = ShowTextures;
    }

    private void OnShowWireframeChanged()
    {
      foreach ( var node in _nodes )
        node.ShowWireframe = ShowWireframe;
    }

    private void OnSearchTermChanged()
      => _searchDebouncer.Invoke();

    private void ApplySearchTerm()
    {
      Dispatcher.BeginInvoke( () =>
      {
        Nodes.Refresh();
        OnPropertyChanged( nameof( AllMeshesVisible ) );
      } );
    }

    #endregion

    #region Overrides

    protected override void OnDisposing()
    {
      base.OnDisposing();
      lock ( _collectionLock )
        if ( _nodes != null )
          _nodes.Clear();

      foreach ( var n in GroupModel.GroupNode.Traverse() )
        n.ForceDispose();
      GroupModel.GroupNode?.ForceDispose();

      foreach ( var n in GroupModel.SceneNode.Traverse() )
        n.ForceDispose();
      GroupModel.SceneNode?.ForceDispose();

      GroupModel.Clear();
      GroupModel.Dispose();
      GroupModel = null;
    }

    #endregion

  }

}
