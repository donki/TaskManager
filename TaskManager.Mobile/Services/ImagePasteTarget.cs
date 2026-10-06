namespace TaskManager.Mobile.Services;

/// <summary>
/// Hace que un cuadro de texto acepte imagenes: «Pegar» en su menu con una imagen copiada, y las
/// imagenes que manda el teclado (Gboard, Samsung…). Cada imagen se le entrega a quien la pidio,
/// que la guarda como adjunto.
/// </summary>
/// <remarks>
/// <para>Un <c>EditText</c> de Android solo sabe pegar texto: con una imagen en el portapapeles
/// «Pegar» no hacia nada, y el teclado decia que la aplicacion no admite imagenes ahi. La via
/// oficial es <c>ViewCompat.SetOnReceiveContentListener</c> con <c>image/*</c>: el
/// <c>AppCompatEditText</c> de MAUI manda por el tanto el pegado como el contenido del teclado
/// (y anuncia al teclado que admite imagenes).</para>
///
/// <para>Lo que no es imagen (el texto de siempre) se devuelve sin tocar y se pega como antes.</para>
/// </remarks>
public static class ImagePasteTarget
{
    public static void Enable(InputView view, Func<byte[], string, Task> onImage)
    {
#if ANDROID
        view.HandlerChanged += (_, _) =>
        {
            if (view.Handler?.PlatformView is Android.Views.View native)
            {
                AndroidX.Core.View.ViewCompat.SetOnReceiveContentListener(native, ["image/*"], new Receiver(onImage));
            }
        };
#endif
    }

#if ANDROID
    private sealed class Receiver(Func<byte[], string, Task> onImage)
        : Java.Lang.Object, AndroidX.Core.View.IOnReceiveContentListener
    {
        public AndroidX.Core.View.ContentInfoCompat? OnReceiveContent(
            Android.Views.View view, AndroidX.Core.View.ContentInfoCompat payload)
        {
            var clip = payload.Clip;
            var context = view.Context;
            if (context is null || clip.ItemCount == 0)
            {
                return payload;
            }

            var hint = PastedImage.ImageMime(clip.Description);
            var rest = new List<Android.Content.ClipData.Item>();
            var images = new List<(byte[] Bytes, string Extension)>();

            // Se lee aqui mismo y no despues: el permiso sobre el content:// que da el teclado
            // dura lo que dura esta llamada.
            for (var i = 0; i < clip.ItemCount; i++)
            {
                var item = clip.GetItemAt(i);
                if (item?.Uri is { } uri && PastedImage.Read(context, uri, hint) is { } image)
                {
                    images.Add(image);
                }
                else if (item is not null)
                {
                    rest.Add(item);
                }
            }

            foreach (var (bytes, extension) in images)
            {
                MainThread.BeginInvokeOnMainThread(async () => await onImage(bytes, extension));
            }

            if (images.Count == 0)
            {
                return payload;
            }

            if (rest.Count == 0)
            {
                return null;
            }

            // Lo que no era imagen sigue su camino: se pega como texto.
            var left = new Android.Content.ClipData(clip.Description, rest[0]);
            foreach (var item in rest.Skip(1))
            {
                left.AddItem(item);
            }

            return new AndroidX.Core.View.ContentInfoCompat.Builder(payload).SetClip(left).Build();
        }
    }
#endif
}
