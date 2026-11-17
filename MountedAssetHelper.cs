using System;
using System.Linq;
using System.Threading.Tasks;
using Editor;
using Sandbox;
using Sandbox.Mounting;

namespace Sandbox.Editor;

/// <summary>
/// Fixes Source 1 / Garry's Mod mounted assets (mount://) in the s&box Editor:
/// 1. Registers mounted game resources in Editor.AssetSystem so native Scene Drag & Drop works.
/// 2. Prevents Win32Exception (Process.Start on 'mount:\gmod\...') when double-clicking mounted assets.
/// 3. Adds right-click context menu options to spawn mounted models as renderers or physics props.
/// </summary>
public static class MountedAssetHelper
{
	[Event( "editor.created" )]
	public static void OnEditorCreated( EditorMainWindow window )
	{
		_ = InitializeAsync();
	}

	[Event( "localaddons.changed" )]
	public static void OnAddonsChanged()
	{
		_ = InitializeAsync();
	}

	public static async Task InitializeAsync()
	{
		await SyncMountedSourcesAsync();
		FixBrowserDoubleClick();
	}

	/// <summary>
	/// Ensures any mounted Source 1 / GMod game (or mount listed in .sbproj) has its virtual
	/// mount:// resources registered inside Editor.AssetSystem so Drag & Drop works natively.
	/// </summary>
	public static async Task SyncMountedSourcesAsync()
	{
		var refreshedAny = false;

		foreach ( var info in Directory.GetAll() )
		{
			var source = Directory.Get( info.Ident );
			if ( source is null || !source.IsInstalled )
				continue;

			bool shouldBeMounted = source.IsMounted ||
				(Project.Current?.Config?.Mounts?.Contains( source.Ident, StringComparer.OrdinalIgnoreCase ) ?? false);

			if ( !shouldBeMounted )
				continue;

			// Check if AssetSystem actually registered the mount's resources
			var sampleResource = source.Resources.FirstOrDefault();
			if ( sampleResource is not null && AssetSystem.FindByPath( sampleResource.Path ) is not null )
				continue;

			await EditorUtility.Mounting.SetMounted( source.Ident, true );
			refreshedAny = true;
		}

		if ( refreshedAny )
		{
			MainAssetBrowser.Instance?.Local?.UpdateAssetList();
			MainAssetBrowser.Instance?.Mounts?.UpdateAssetList();
		}
	}

	public static void FixBrowserDoubleClick()
	{
		var browser = MainAssetBrowser.Instance;
		if ( browser is null )
			return;

		if ( browser.Local is not null )
		{
			// Prevent LocalAssetBrowser from calling Process.Start("C:\...\mount:\gmod\...")
			// when double-clicking a mount:// entry or procedural mounted asset.
			browser.Local.OnAssetSelected = asset =>
			{
				if ( asset is null )
					return;

				if ( asset.CanOpenInEditor )
				{
					asset.OpenInEditor();
				}
				else if ( MountUtility.IsMountPath( asset.Path ) && asset.AssetType == AssetType.Model )
				{
					_ = SpawnMountedModelAsync( asset.Path, asProp: false );
				}
			};

			browser.Local.OnFileSelected = file =>
			{
				if ( TryExtractMountUri( file, out var mountUri ) )
				{
					_ = SpawnMountedModelAsync( mountUri, asProp: false );
					return;
				}

				EditorUtility.OpenFile( file );
			};
		}

		if ( browser.Mounts is not null )
		{
			browser.Mounts.OnAssetSelected = asset =>
			{
				if ( asset is null )
					return;

				if ( asset.CanOpenInEditor )
				{
					asset.OpenInEditor();
				}
				else if ( MountUtility.IsMountPath( asset.Path ) && asset.AssetType == AssetType.Model )
				{
					_ = SpawnMountedModelAsync( asset.Path, asProp: false );
				}
			};

			browser.Mounts.OnFileSelected = file =>
			{
				if ( TryExtractMountUri( file, out var mountUri ) )
				{
					_ = SpawnMountedModelAsync( mountUri, asProp: false );
					return;
				}

				EditorUtility.OpenFile( file );
			};
		}
	}

	[Event( "asset.contextmenu", Priority = 20 )]
	public static void OnAssetContextMenu( AssetContextMenu e )
	{
		if ( e.SelectedList is null || e.SelectedList.Count == 0 )
			return;

		var modelPaths = e.SelectedList
			.Select( GetEntryMountOrAssetPath )
			.Where( p => !string.IsNullOrEmpty( p ) && p.EndsWith( ".vmdl", StringComparison.OrdinalIgnoreCase ) )
			.ToList();

		if ( modelPaths.Count == 0 )
			return;

		e.Menu.AddSeparator();

		e.Menu.AddOption(
			modelPaths.Count == 1 ? "Spawn Model in Scene" : $"Spawn {modelPaths.Count} Models in Scene",
			"view_in_ar",
			() =>
			{
				foreach ( var path in modelPaths )
				{
					_ = SpawnMountedModelAsync( path, asProp: false );
				}
			} );

		e.Menu.AddOption(
			modelPaths.Count == 1 ? "Spawn as Physics Prop" : $"Spawn {modelPaths.Count} as Physics Props",
			"deployed_code",
			() =>
			{
				foreach ( var path in modelPaths )
				{
					_ = SpawnMountedModelAsync( path, asProp: true );
				}
			} );
	}

	private static string GetEntryMountOrAssetPath( AssetEntry entry )
	{
		if ( entry.Asset is not null )
			return entry.Asset.Path;

		var raw = entry.FileInfo?.ToString();
		if ( !string.IsNullOrEmpty( raw ) && MountUtility.IsMountPath( raw ) )
			return raw;

		if ( TryExtractMountUri( entry.AbsolutePath, out var mountUri ) )
			return mountUri;

		return null;
	}

	private static bool TryExtractMountUri( string path, out string mountUri )
	{
		mountUri = null;
		if ( string.IsNullOrEmpty( path ) )
			return false;

		var normalized = path.Replace( '\\', '/' );
		if ( normalized.StartsWith( "mount://", StringComparison.OrdinalIgnoreCase ) )
		{
			mountUri = normalized;
			return true;
		}

		var idx = normalized.IndexOf( "/mount:/", StringComparison.OrdinalIgnoreCase );
		if ( idx >= 0 )
		{
			mountUri = "mount://" + normalized[(idx + "/mount:/".Length)..].TrimStart( '/' );
			return true;
		}

		return false;
	}

	public static async Task<GameObject> SpawnMountedModelAsync( string path, bool asProp )
	{
		var session = SceneEditorSession.Active;
		if ( session?.Scene is null )
			return null;

		Model model = null;

		if ( MountUtility.IsMountPath( path ) )
		{
			// Format: mount://<ident>/<relative_path>
			var withoutScheme = path["mount://".Length..];
			var slashIdx = withoutScheme.IndexOf( '/' );
			if ( slashIdx > 0 )
			{
				var ident = withoutScheme[..slashIdx];
				var mount = Directory.Get( ident );
				if ( mount is not null )
				{
					if ( !mount.IsMounted || AssetSystem.FindByPath( path ) is null )
					{
						await EditorUtility.Mounting.SetMounted( ident, true );
					}

					var loader = mount.Resources.FirstOrDefault(
						r => string.Equals( r.Path, path, StringComparison.OrdinalIgnoreCase ) );

					if ( loader is not null )
					{
						model = (await loader.GetOrCreate()) as Model;
					}
				}
			}
		}

		model ??= await Model.LoadAsync( path );
		if ( model is null )
		{
			Log.Warning( $"[MountedAssetHelper] Could not load model: {path}" );
			return null;
		}

		using var sceneScope = session.Scene.Push();
		using ( session.UndoScope( "Spawn Mounted Model" ).WithGameObjectCreations().Push() )
		{
			var spawnPos = Vector3.Zero;
			var cam = session.Scene.Camera;
			if ( cam is not null )
			{
				var tr = session.Scene.Trace
					.Ray( cam.WorldPosition, cam.WorldPosition + cam.WorldRotation.Forward * 200f )
					.Run();
				spawnPos = tr.Hit ? tr.HitPosition : cam.WorldPosition + cam.WorldRotation.Forward * 100f;
			}

			var cleanName = System.IO.Path.GetFileNameWithoutExtension( path.Replace( ".mdl.vmdl", ".vmdl" ) );
			var go = new GameObject( true, cleanName );
			go.WorldPosition = spawnPos;

			if ( asProp && (model.Physics?.Parts.Count ?? 0) > 0 )
			{
				var prop = go.Components.Create<Prop>();
				prop.Model = model;
			}
			else if ( model.BoneCount > 1 )
			{
				var skinned = go.Components.Create<SkinnedModelRenderer>();
				skinned.Model = model;
			}
			else
			{
				var renderer = go.Components.Create<ModelRenderer>();
				renderer.Model = model;
			}

			EditorScene.Selection.Clear();
			EditorScene.Selection.Add( go );
			return go;
		}
	}
}
