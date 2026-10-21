using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Index.Domain.Assets;
using Index.Modules.DataExplorer.ViewModels;

namespace Index.Modules.DataExplorer.Services
{

  public class AssetNodeViewModelFactory
  {

    #region Data Members

    private readonly IAssetManager _assetManager;

    #endregion

    #region Constructor

    public AssetNodeViewModelFactory( IAssetManager assetManager )
    {
      _assetManager = assetManager;
    }

    #endregion

    #region Public Methods

    public List<AssetNodeViewModel> CreateNodes()
    {
      var rootCategories = new List<AssetNodeViewModel>();

      foreach ( var referenceCollection in _assetManager.ReferenceCollections )
        rootCategories.Add( CreateCategoryNode( referenceCollection ) );

      rootCategories.Sort( ( a, b ) => a.Name.CompareTo( b.Name ) );
      return rootCategories;
    }

    public List<AssetNodeViewModel> CreateNodeSearchGraph( IEnumerable<AssetNodeViewModel> nodes )
    {
      var flattenedList = new List<AssetNodeViewModel>();

      void FlattenNode( AssetNodeViewModel node, List<AssetNodeViewModel> result )
      {
        foreach ( var child in node.Children )
        {
          FlattenNode( child, result );
        }

        result.Add( node );
      }

      foreach ( var root in nodes )
      {
        FlattenNode( root, flattenedList );
      }

      return flattenedList;
    }

    #endregion

    /// <summary>
    ///   Builds a nested folder tree from the asset names (e.g. "Game/Characters/Hero/Marcus/T_Marcus_D").
    /// </summary>
    private AssetNodeViewModel CreateCategoryNode( IAssetReferenceCollection assetReferenceCollection )
    {
      var categoryNode = new AssetNodeViewModel( assetReferenceCollection.AssetTypeName );
      categoryNode.AssetType = assetReferenceCollection.AssetType;

      var folders = new Dictionary<string, AssetNodeViewModel>( StringComparer.OrdinalIgnoreCase );
      foreach ( var asset in assetReferenceCollection.Where( x => !x.Node.IsHidden ) )
      {
        var parts = asset.AssetName.Split( '/', '\\' );
        var parent = categoryNode;
        var folderPath = string.Empty;

        for ( var i = 0; i < parts.Length - 1; i++ )
        {
          folderPath = folderPath + "/" + parts[ i ];
          if ( !folders.TryGetValue( folderPath, out var folder ) )
          {
            folder = new AssetNodeViewModel( parts[ i ] );
            parent.AddChild( folder );
            folders.Add( folderPath, folder );
          }

          parent = folder;
        }

        parent.AddChild( new AssetNodeViewModel( asset, parts[ ^1 ] ) );
      }

      SortChildren( categoryNode );
      return categoryNode;
    }

    private static void SortChildren( AssetNodeViewModel node )
    {
      if ( node.Children.Count == 0 )
        return;

      var sorted = node.Children
        .OrderBy( x => x.IsLeaf )
        .ThenBy( x => x.Name, StringComparer.OrdinalIgnoreCase )
        .ToList();

      node.Children.SuppressNotifications = true;
      node.Children.Clear();
      foreach ( var child in sorted )
      {
        node.Children.Add( child );
        SortChildren( child );
      }
      node.Children.SuppressNotifications = false;
    }

  }

}
