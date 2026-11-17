# s&box Mounted Assets Fix (`mount://gmod/...` Drag & Drop + Double-Click Fix)

Fixes the `Win32Exception: Не удается найти указанный файл / The system cannot find the file specified` error and broken Scene Drag & Drop when working with mounted **Garry's Mod / Half-Life 2 / Source 1** assets (`mount://gmod/...`, `mount://hl2/...`, `mount://cstrike/...`) in the **s&box Editor**.

---

## EN — English

### The Problem

When browsing mounted games (such as **Garry's Mod**, **Half-Life 2**, or **Counter-Strike: Source**) in the s&box Editor's **Asset Browser**, attempting to double-click or drag-and-drop a mounted `.mdl.vmdl` asset into the scene can fail with:

```text
[Interop] Error: An error occurred trying to start process
'C:\Program Files (x86)\Steam\steamapps\common\sbox\mount:\gmod\models\items\battery.mdl.vmdl'
with working directory 'C:\Program Files (x86)\Steam\steamapps\common\sbox'.
The system cannot find the file specified.
```

#### Root Cause
1. In `MountLocation.GetFiles()`, the s&box Editor wraps virtual `mount://gmod/...` URIs into standard Windows `new System.IO.FileInfo("mount://...")` objects.
2. If a mount was initialized via `Sandbox.Mounting.Directory.Mount(...)` without calling `EditorUtility.Mounting.SetMounted(...)` / `AssetSystem.AddAssetsFromMount(...)`, `Editor.AssetSystem.FindByPath("mount://...")` returns `null`.
3. Because `AssetEntry.Asset` is `null` (or `CanOpenInEditor` is `false` on procedural mounted assets in `LocalAssetBrowser`), `FileInfo.FullName` resolves `mount://...` as a relative Windows path inside the engine directory (`...\sbox\mount:\gmod\...`).
4. Double-clicking invokes `Process.Start()` on that invalid path, and dragging into the Scene View fails because `ModelDropObject` cannot resolve the unregistered `Asset`.

### What This Fix Does

- **Fixes Native Scene Drag & Drop**: Automatically synchronizes mounted Source 1 / Garry's Mod games with `Editor.AssetSystem` (`EditorUtility.Mounting.SetMounted`) on editor startup so `ModelDropObject` resolves `mount://` assets properly and generates thumbnails.
- **Fixes Double-Click Error**: Intercepts `OnFileSelected` / `OnAssetSelected` on `mount://` paths so double-clicking a mounted model spawns it into the active scene instead of crashing in `Process.Start()`.
- **Adds Right-Click Context Menu**: Adds two convenient options when right-clicking any `.vmdl` in the Asset Browser:
  - **`Spawn Model in Scene`** (`SkinnedModelRenderer` / `ModelRenderer`)
  - **`Spawn as Physics Prop`** (`Prop` component with physics)
- **100% Safe for Workshop & Engine Updates**: Lives inside your project's `Editor/` folder (`*.editor.csproj`). It is never included in client/server runtime builds and is never overwritten when s&box updates.

### Installation

1. Copy the [`Editor/MountedAssetHelper.cs`](Editor/MountedAssetHelper.cs) file into the `Editor/` folder of your s&box project:
   ```text
   <your_sbox_project>/
   ├── Assets/
   ├── Code/
   └── Editor/
       └── MountedAssetHelper.cs
   ```
2. Ensure `"gmod"` (or `"hl2"`, `"cstrike"`, etc.) is enabled in your `.sbproj` under `"Mounts"`, or right-click **Garry's Mod** in the **Mounts** tab of the Asset Browser and check **Mounted**:
   ```json
   "Mounts": [
     "gmod"
   ]
   ```

---

## RU — Русский

### В чём проблема

При попытке заспавнить или открыть двойным кликом модель, подключённую через систему маунтов (**Garry's Mod**, **Half-Life 2**, **CS:S**), в консоли редактора s&box появляется ошибка, а Drag & Drop на сцену не срабатывает:

```text
[Interop] Error: An error occurred trying to start process
'C:\Program Files (x86)\Steam\steamapps\common\sbox\mount:\gmod\models\items\battery.mdl.vmdl'
with working directory 'C:\Program Files (x86)\Steam\steamapps\common\sbox'.
Не удается найти указанный файл.
```

#### Причина бага в редакторе s&box
1. В `MountLocation.GetFiles()` редактор оборачивает виртуальный URI `mount://gmod/...` в стандартный Windows-класс `new System.IO.FileInfo("mount://...")`.
2. Если маунт был подключён в рантайме без регистрации в `Editor.AssetSystem` (`EditorUtility.Mounting.SetMounted`), `AssetSystem.FindByPath("mount://...")` возвращает `null`.
3. Стандартный `FileInfo.FullName` в Windows не знает протокол `mount://`, считает его относительным путём от рабочей папки движка и превращает в несуществующий путь `C:\Program Files (x86)\Steam\steamapps\common\sbox\mount:\gmod\...`.
4. При двойном клике редактор пытается запустить этот путь через системный `Process.Start()`, а при перетаскивании на сцену `ModelDropObject` получает `null` и отменяет спавн.

### Что делает этот фикс

- **Чинит обычный Drag & Drop мышкой на сцену**: автоматически синхронизирует подключённые маунты (`gmod`, `hl2`, `cstrike` и др.) с `Editor.AssetSystem` при запуске редактора, благодаря чему превью-иконки и перетаскивание на сцену начинают работать штатно.
- **Убирает ошибку при двойном клике**: перехватывает клик по `mount://` путям в Asset Browser и вместо вызова `Process.Start()` спавнит модель в активную сцену перед камерой.
- **Добавляет удобное меню по ПКМ**: при правом клике на любую `.vmdl` модель в браузере ассетов появляются кнопки:
  - **`Spawn Model in Scene`** — заспавнить модель (`SkinnedModelRenderer` / `ModelRenderer`);
  - **`Spawn as Physics Prop`** — заспавнить как физический проп (`Prop`).
- **Не влияет на публикацию в Workshop и не слетает при обновлениях**: скрипт работает только внутри сборки `.editor` вашего проекта, не попадает в релизный билд игры и не перезаписывается при обновлениях самого движка s&box.

### Установка

1. Скопируйте файл [`Editor/MountedAssetHelper.cs`](Editor/MountedAssetHelper.cs) в папку `Editor/` вашего проекта s&box:
   ```text
   <папка_вашего_проекта>/
   ├── Assets/
   ├── Code/
   └── Editor/
       └── MountedAssetHelper.cs
   ```
2. Убедитесь, что в вашем `.sbproj` файле указан нужный маунт (или включите галочку **Mounted** по ПКМ на игру во вкладке **Mounts** в Asset Browser):
   ```json
   "Mounts": [
     "gmod"
   ]
   ```
