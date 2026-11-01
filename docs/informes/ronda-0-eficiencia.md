# Ronda: eficiencia del flujo de trabajo (chore/eficiencia)

## Cambios
- **CLAUDE.md** (solo local): sección "Reglas de trabajo" con las 10 reglas, textuales.
- **ci.yml**: `concurrency` con cancel-in-progress por rama; `paths-ignore` (docs/**, *.md, legacy-ps/**, LICENSE) en push y pull_request;
  caché de paquetes NuGet con la opción `cache` de `actions/setup-dotnet` (acción que ya se usaba; no se agregó ninguna de terceros).
  El trabajo de integración salió de este archivo; `publish` ahora depende solo de `build-test`.
- **integration.yml** (nuevo): pruebas con ffmpeg real. Corre si cambian `Engine/`, `Tools/`, las pruebas `*Integration*`, `Fixtures/`, `FakeYtDlp/` o el
  propio workflow, y a mano (`workflow_dispatch`). Esta ronda lo dispara por tocar el propio workflow.
- **scripts/check.ps1**: compila + pruebas rápidas (sin Integration ni Inestable), muestra resumen y segundos. Probado: 522 + 206 pruebas, 46 s.
- **docs/ARQUITECTURA.md**: mapa de carpetas y clases (1 página).
- **.gitignore**: `TestResults*/` y `screenshots/`. No había carpetas viejas de compilación o capturas dentro del repo (solo ignoradas bin/obj/dist).
- Se borró un `pyyaml-6.0.3.tar.gz` suelto en la raíz (sin seguir en git; lo había bajado yo por un intento fallido).

## Pruebas inestables
Suite completa (con ffmpeg real) 3 veces seguidas en local: **0 fallas en las 3** (Core 567 ok + 2 omitidas, App 206 ok; ~52 s cada una).
No hay pruebas para marcar `[Trait("Inestable","si")]`; el filtro `Inestable!=si` ya está en el CI y en check.ps1 para cuando haga falta.
**Reintento quitado** (a pedido de Nico, regla 8): el CI corre las pruebas una sola vez. Si vuelve a aparecer el error de limpieza de Avalonia ("different thread owns it"), se investiga o se marca `Inestable`, no se reintenta.
