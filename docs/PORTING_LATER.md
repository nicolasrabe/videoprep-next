# Pendiente para portar a Mac / Linux

Desde 0.3.1 el CI y la publicación son solo Windows. El código de Mac/Linux sigue en el repositorio (abstracción `Platform`,
pruebas de rutas, target `net10.0` sin `-windows`), pero **nada de eso se compila, prueba ni publica fuera de Windows**.
Puede haberse estropeado sin que nadie lo note.

## Para volver a tener Mac/Linux
1. **CI**: agregar de nuevo `macos-latest` y `ubuntu-latest` a `build-test` en `.github/workflows/ci.yml` (la versión anterior
   está en el historial de git, commit `d604d6c`). Hay que mirar qué pruebas fallan después de este tiempo.
2. **Pruebas de integración con ffmpeg real**: hoy corren en Windows con el ffmpeg win64 de yt-dlp/FFmpeg-Builds. Para Linux
   existe el gemelo `linux64-gpl` (no el paquete de Ubuntu: da "Error parsing Opus packet header", ver `docs/VERIFY.md`).
   Para Mac no hay ffmpeg en esa fuente: hace falta otra fuente.
3. **Publicación**: agregar `osx-arm64` (y `linux-x64` si se quiere) a `release.yml` y al `publish` del CI; el zip
   `videoprep-<tag>-<rid>.zip` tiene que quedar en `SHA256SUMS.txt`.
4. **Programas externos** (`ToolManager`): hoy descarga yt-dlp, ffmpeg y deno de Windows. Hay que elegir los de cada sistema.
5. **Actualizador**: en Mac/Linux solo abre la página de descargas. Falta reemplazo del ejecutable, permisos de ejecución y
   la cuarentena de Gatekeeper en Mac.
6. **Tarea programada** (revisar con videoprep cerrado): solo existe la de Windows; en Mac sería `launchd`, en Linux `cron`/`systemd`.
7. **Firma y notarización** en Mac, o el programa no abre sin avisos.
8. **Probar a mano** en un Mac y un Linux reales: la interfaz, carpetas de datos, abrir carpeta/navegador, cookies del navegador.
