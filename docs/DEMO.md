# Modo demostración (`--demo`)

Sirve para ver el motor de la 0.2 funcionando **antes de que existan las pantallas nuevas**. Descarga un video,
corta un fragmento, lo revisa y muestra cada paso en la terminal, en español simple.

- Usa yt-dlp y ffmpeg de verdad.
- **No lee las cookies de ningún navegador** (descarga sin sesión iniciada).
- No cambia la configuración de videoprep (`settings.json`) ni la cola.
- Escribe un registro detallado en inglés en `%LOCALAPPDATA%\videoprep\logs\videoprep-AAAAMMDD.log`.

## Cómo se usa

Desde la carpeta del proyecto, en la terminal de VS Code:

```
dotnet run --project src/Videoprep.App -- --demo "<enlace>" <inicio> <fin>
```

- `<enlace>`: un enlace de YouTube (o de otro sitio que soporte yt-dlp), entre comillas.
- `<inicio>` y `<fin>`: código de tiempo como en Premiere, `HH:MM:SS:FF`. El fotograma de fin **se incluye**.
  Se puede omitir `<fin>` para ir hasta el final. También sirven `-00:00:00:15` (los últimos 15 fotogramas) y
  el atajo de números (`523` = `00:00:05:23`).

Opciones:

| Opción | Qué hace | Si no se pone |
|---|---|---|
| `--carpeta <carpeta>` | Dónde guardar el resultado | `Videos\videoprep-demo` |
| `--formato <formato>` | `mp4`, `movh`, `movp`, `webm`, `mkv`, `m4a`, `mp3`, `wav`, `flac`, `opus` | `mp4` |
| `--resolucion <res>` | Resolución máxima a descargar: `480p`, `720p`, `1080p` (y `1440p`, `4K` para ProRes/WebM/MKV) | `1080p` |
| `--herramientas <carpeta>` | Carpeta donde están yt-dlp, ffmpeg, ffprobe y deno | `%LOCALAPPDATA%\videoprep\tools`, la carpeta del programa y el PATH |
| `--descargar-herramientas` | Si faltan, las descarga (unos 200 MB, una sola vez) a la carpeta de herramientas | No descarga nada y explica qué falta |

Para usar las herramientas que ya están en el proyecto (`.tools/`):

```
dotnet run --project src/Videoprep.App -- --demo "<enlace>" 00:00:05:00 00:00:07:29 --herramientas .tools
```

## Video de prueba sugerido

"Big Buck Bunny 60fps 4K" de Blender Foundation, `https://www.youtube.com/watch?v=aqz-KE-bpKQ` (10:35). YouTube
lo marca como "Creative Commons Attribution license (reuse allowed)" (dato leído con yt-dlp el 2026-10-26). La
demostración baja el video entero antes de cortar: con `--resolucion 480p` pesa mucho menos.

## Qué se ve

```
Paso 1 de 5 - Busco las herramientas (yt-dlp, ffmpeg, ffprobe y deno)...
Paso 2 de 5 - Preparo el pedido
Paso 3 de 5 - Descargo el video completo (sin cookies)...
    41,5 % a 7.16MiB/s, faltan 00:03
Paso 4 de 5 - Corto el fragmento con ffmpeg
   Del fotograma 150 al 239: 90 fotogramas a 29,97 por segundo (3,003 s).
Paso 5 de 5 - Reviso el resultado con ffprobe antes de tocar nada...
   Bien: tiene 90 fotogramas, 3,003 s, como se pidió.

Listo. Archivo final:
   C:\Users\...\Videos\videoprep-demo\<título del video>.mp4
El video completo que se descargó para cortar ya se borró (solo después de revisar el fragmento).
```

Si el resultado no pasa la revisión, **el video descargado se conserva** y se avisa por qué. `Ctrl+C` detiene todo
(se cierra también cualquier proceso que yt-dlp haya abierto).

Código de salida: `0` = bien, `1` = algo falló, `2` = uso incorrecto o faltan herramientas.

## Con el programa publicado

`dist\win-x64\videoprep.exe --demo ...` también funciona. Como es un programa con ventana, la terminal puede
mostrar el texto mezclado con el cursor; con `dotnet run` se ve prolijo.

## Para desarrolladores: probarlo sin internet

Las pruebas usan un yt-dlp falso (`tests/FakeYtDlp`). Para correr la demostración con él: copiá
`fake-yt-dlp.*` desde `tests/Videoprep.Core.Tests/bin/Debug/net10.0/` a una carpeta, copiá también
`fake-yt-dlp.exe` como `yt-dlp.exe`, agregá ffmpeg, ffprobe y deno, definí `FAKE_YTDLP_SOURCE` con la ruta de un video
de prueba y usá un enlace `https://fake.test/ok/<id>` con `--herramientas <esa carpeta>`.
