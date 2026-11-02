# videoprep 0.3.2 (versión de prueba / test release, Windows)

## Español

**Importante:** las versiones 0.3.0 y 0.3.1 ya instaladas no pueden recibir este arreglo solas: bajá `videoprep-v0.3.2-win-x64.zip` de abajo, descomprimilo y reemplazá tu `videoprep.exe` a mano una vez (tus ajustes, tu cola y tus suscripciones se conservan). Desde la 0.3.2, el aviso de versión nueva funciona al abrir.

### Novedades
- **Aviso de versión nueva al abrir**: se vuelve a buscar si pasaron 6 horas desde la última búsqueda exitosa, y siempre mientras se sepa que hay una versión más nueva. El aviso dice cuál es la nueva y cuál tenés, con el botón **Actualizar** bien visible. Los errores de red solo se anotan en el Registro.

### Cosas que conviene saber
- El programa no está firmado: la primera vez Windows puede mostrar "Windows protegió su PC" (Más información → Ejecutar de todas formas).
- Para comprobar el zip: `Get-FileHash videoprep-v0.3.2-win-x64.zip` en PowerShell y compará con `SHA256SUMS.txt`.
- Usalo solo con contenido propio, con licencia o con permiso para usarlo.

## English

**Important:** the already installed 0.3.0 and 0.3.1 cannot receive this fix by themselves: download `videoprep-v0.3.2-win-x64.zip` below, unzip it and replace your `videoprep.exe` by hand once (your settings, queue and subscriptions are kept). From 0.3.2 on, the new-version notice works when the program opens.

### What's new
- **New-version notice on start**: it looks again when 6 hours have passed since the last successful check, and always while a newer version is known. The notice says which version is new and which you have, with a clear **Update** button. Network errors are only written to the Log.

### Good to know
- The program is not signed: the first time Windows may show "Windows protected your PC" (More info -> Run anyway).
- To check the zip: `Get-FileHash videoprep-v0.3.2-win-x64.zip` in PowerShell and compare with `SHA256SUMS.txt`.
- Use it only with your own content, licensed content, or content you have permission to use.
