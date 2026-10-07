using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Database;
using Android.Graphics;
using Android.OS;

namespace TaskManager.UITests.Portapapeles;

/// <summary>
/// Deja una imagen PNG en el portapapeles y se cierra. Se lanza desde las pruebas:
/// <c>am start -n com.socratic.taskmanager.pruebas.portapapeles/.CopiarImagenActivity [--es tipo no]</c>.
/// </summary>
/// <remarks>
/// Con <c>--es tipo no</c> el proveedor no dice el tipo (como hacen algunas apps), para probar que
/// Task Manager lo saca de los bytes. El portapapeles se escribe al tener el foco: desde Android 10
/// solo la app de delante puede tocarlo.
/// </remarks>
[Activity(Name = "com.socratic.taskmanager.pruebas.portapapeles.CopiarImagenActivity", Exported = true,
    Theme = "@android:style/Theme.Translucent.NoTitleBar", NoHistory = true,
    LaunchMode = LaunchMode.SingleTask)]
public sealed class CopiarImagenActivity : Activity
{
    private bool _hecho;

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (!hasFocus || _hecho)
        {
            return;
        }

        _hecho = true;
        var sinTipo = Intent?.GetStringExtra("tipo") == "no";

        // Un cuadrado rojo de 64x64: lo justo para que sea un PNG de verdad.
        var file = new Java.IO.File(FilesDir, "prueba.png");
        using (var bitmap = Bitmap.CreateBitmap(64, 64, Bitmap.Config.Argb8888!)!)
        {
            bitmap.EraseColor(Android.Graphics.Color.Red);
            using var output = System.IO.File.Create(file.AbsolutePath);
            bitmap.Compress(Bitmap.CompressFormat.Png!, 100, output);
        }

        var uri = Android.Net.Uri.Parse($"content://{ImagenProvider.Autoridad}/{(sinTipo ? "sin-tipo" : "prueba.png")}")!;
        var clipboard = (ClipboardManager)GetSystemService(ClipboardService)!;
        clipboard.PrimaryClip = ClipData.NewUri(ContentResolver, "imagen de prueba", uri);
        Android.Util.Log.Info("TMPortapapeles", $"imagen en el portapapeles: {uri} ({new FileInfo(file.AbsolutePath).Length} bytes)");
        Finish();
    }
}

/// <summary>
/// Sirve el PNG al que lo pegue. No se exporta: el sistema le presta el permiso a quien lea el
/// portapapeles (grantUriPermissions), igual que el FileProvider de Chrome o de la Galeria.
/// </summary>
[ContentProvider([ImagenProvider.Autoridad], Name = "com.socratic.taskmanager.pruebas.portapapeles.ImagenProvider",
    Exported = false, GrantUriPermissions = true)]
public sealed class ImagenProvider : ContentProvider
{
    public const string Autoridad = "com.socratic.taskmanager.pruebas.portapapeles.imagen";

    public override bool OnCreate() => true;

    public override string? GetType(Android.Net.Uri uri) =>
        uri.LastPathSegment == "sin-tipo" ? null : "image/png";

    public override ParcelFileDescriptor? OpenFile(Android.Net.Uri uri, string mode)
    {
        var file = new Java.IO.File(Context!.FilesDir, "prueba.png");
        return ParcelFileDescriptor.Open(file, ParcelFileMode.ReadOnly);
    }

    public override ICursor? Query(Android.Net.Uri uri, string[]? projection, string? selection, string[]? selectionArgs, string? sortOrder) => null;
    public override Android.Net.Uri? Insert(Android.Net.Uri uri, ContentValues? values) => null;
    public override int Delete(Android.Net.Uri uri, string? selection, string[]? selectionArgs) => 0;
    public override int Update(Android.Net.Uri uri, ContentValues? values, string? selection, string[]? selectionArgs) => 0;
}
