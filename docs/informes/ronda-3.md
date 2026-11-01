# Ronda 3 de 0.3.1 - Suscripciones y cierre (rama feat/v0.3.1-subs-cierre)

Spec: `docs/V0_3_1_SPEC.md` (secciones 0 y RONDA 3). La ronda 2 ya estaba mergeada en `main` (commit `70b4f09`, CI en verde) antes de empezar.

## Cómo está hoy Suscripciones frente a lo que supone el spec
- **Coincide**: vive dentro de la pestaña Scraping (botones Explorar | Suscripciones), "Suscribirme a esto..." desde Explorar y "Agregar" abren el mismo diálogo, y los filtros del diálogo usan **el mismo código** (`FilterInputs`) que Explorar.
- **Es distinto**:
  1. Suscripciones **no tiene ninguna lista de videos con casillas**: es una tabla de suscripciones (con "Activa", último control, etc.). Por eso **no hay filas expandibles ni grupo "Selección" que aplicar** ahí, y no inventé una lista. Si se quiere ver qué se bajó en cada revisión habría que agregar esa lista (decisión de Nico).
  2. "Quitar suscripción" con confirmación **ya existía** (y no hay botón Reiniciar); no se tocó.
  3. Las revisiones corren **solas** (al abrir, cada N horas, con el programa cerrado), así que los avisos de 50 videos y de 150 pedidos **no pueden preguntar**: se aplican como topes que no preguntan (ver abajo).
  4. La primera revisión ofrecía "Solo lo nuevo desde ahora" o "También los más viejos que pasen los filtros" (esta última bajaba hasta 100 videos de historial).
- Antes de tocar nada, las revisiones listaban hasta 100 videos por pestaña en cada control, sin cortar, y dejaban pasar fechas sin confirmar (el mismo bug de fechas).

## Lo hecho
- **Menos carga**: `CheckRunner` ahora usa la misma búsqueda que Scraping (`ScrapeSearch`): un listado por pestaña de lo nuevo a lo viejo, **cortado al llegar al primer video que el historial de esa suscripción ya tiene** (`StopAtKnown`), más corte por fecha. Una revisión normal cuesta una o dos páginas.
  Las revisiones ya eran una por una con pausa de 5 a 15 s entre suscripciones y un tope de videos nuevos por revisión (probado: pausa entre cada par, nunca todas a la vez).
- **Fechas**: misma regla estricta; lo que no se puede confirmar **no se baja**, se cuenta aparte ("{0}: N videos sin fecha confirmada (no se bajaron)" en la línea de estado y en el registro). Lo que ya se preguntó y sigue sin fecha, o cuya fecha exacta cayó fuera del rango, se anota como visto para no preguntarlo en cada revisión.
- **Topes sin preguntar**: 150 pedidos de fecha exacta por suscripción y revisión (lo que sobra espera a la próxima); a los 50 candidatos la lectura se detiene; si el sitio informa un total mayor a 50, se limita a 50.
- **Primera revisión de una suscripción nueva** (nunca baja todo el historial): **Desde hoy** (por defecto; lo que ya existe cuenta como visto y se lee solo una página), **Desde hace 7 días** o **Desde esta fecha** (se guarda la fecha; lo más viejo que se miró se anota como visto).
  La opción vieja "también los más viejos" se sacó del diálogo (las suscripciones ya creadas con ella siguen funcionando, con tope de 50).
- **Decisión de diseño que evita perder videos**: cuando hay más para bajar que el cupo de la revisión (25 por defecto), se bajan **los más viejos primero** (y, con el tope de fechas, también se confirman primero los más viejos). Como la próxima revisión corta al llegar al primer video conocido,
  si se bajaran los más nuevos primero los más viejos quedarían tapados detrás y nunca se bajarían. Está probado con las dos situaciones (cupo y tope de fechas).
- Pruebas nuevas: 6 de revisión (primera revisión desde fecha, desde hoy con una sola página y corte, fecha sin confirmar, tope sin preguntar con segunda revisión, pausas, formato guardado), 1 de la búsqueda con corte, 3 de pantalla (las tres opciones y fecha mala, editar no vuelve a preguntar, quitar pregunta primero).

## Cierre de la versión (3.2)
- **Capturas**: 120 imágenes (10 pantallas x 6 temas x 2 idiomas) en `docs/capturas/0.3.1/`: Prep sin y con fragmentos, Cola con una fila abierta, Scraping (enlace, filtros, resultados con fila abierta), Suscripciones, diálogo de suscripción, ficha y barra mini.
  Las hace una sola prueba a mano (`--filter "Capturas=si"`), que además revisa que ninguna tenga un texto sin traducir; no corre en el CI.
- **CHANGELOG** ("Sin publicar (0.3.1)"), `docs/VERIFY.md` y `docs/ARQUITECTURA.md` al día. README y la versión en `Directory.Build.props` **no se tocaron** (siguen en 0.3.0 hasta que Nico diga "publicá").
- **.exe para probar**: `dist\win-x64-0.3.1-prueba\videoprep.exe` (48 MB, `-p:VersionPrefix=0.3.1 -p:VersionSuffix=prueba`, así dice "0.3.1-prueba" y no se hace pasar por la versión publicada).
  SHA-256: `9bc90e79c41f6fab0cc255bb1df0376d9bacb32984c61c2010fe5ae6c9618e57`. Se publicó (compila y empaqueta) pero **yo no lo abrí**: lo que sé de él viene de las pruebas, no de haberlo visto andar en un escritorio.
- **No se publicó ni se creó ningún tag.** Espero el "publicá".

## Pruebas marcadas Inestables, corridas a mano
2 pruebas de pantalla de 12 combinaciones (24 casos): `Settings_and_download_with_the_new_options` y `Queue_dialog_and_subscriptions_with_profiles` (la tercera, `Mini_window_in_its_states`, se borró en la ronda 2).
**3 corridas: 71 de 72 casos pasaron.** El que falló (una vez, `Settings_and_download_with_the_new_options`) fue otra vez
`[Test Case Cleanup Failure ...]: System.InvalidOperationException : The calling thread cannot access this object because a different thread owns it` dentro de `Avalonia.Headless ... EnsureIsolatedApplication`: **código de la librería de pruebas, no de la app**. Queda anotado; no hay nada que arreglar en videoprep. Siguen fuera del CI.

## Suite
Local: **576 (núcleo) + 214 (app)** pruebas normales, todas verdes. Prueba real de ffmpeg (fragmentos con nombre) verde.

## Lista para probar el .exe (8 pasos)
1. Abrí `D:\videoprep\videoprep-v2-kit\dist\win-x64-0.3.1-prueba\videoprep.exe` (si Windows dice "protegió su PC": "Más información" > "Ejecutar de todas formas"). En Ajustes tiene que decir versión **0.3.1-prueba**.
2. **Pestañas**: tienen un ícono cada una, la primera se llama **Prep**, y arriba a la derecha está el botón **Modo mini**.
3. **Prep**: pegá un enlace de YouTube (o apretá **Pegar** con uno copiado): aparece la vista previa grande. Apretá **+ Agregar fragmento**: "Nombre del archivo" se apaga y avisa por qué.
   Poné inicio y fin en dos fragmentos, un nombre en el primero y dejá vacío el segundo; **Enviar a la cola** y mirá la pestaña Cola: tiene que decir tu nombre y "Título - parte 2".
4. **Modo mini**: apretá **Modo mini**: aparece una ficha arriba a la derecha de tu monitor. Apretá **▁**: queda una barra con "Copiá un enlace para descargar". **Doble clic** en la barra: vuelve la ficha. **Doble clic** en el título de la ficha: vuelve la ventana completa, donde estaba. (Un doble clic sobre un botón o una caja no tiene que hacer nada.)
5. **Dos monitores (si tenés)**: llevá la ventana completa al segundo monitor y repetí el paso 4: la ficha y la barra tienen que aparecer en **ese** monitor, arriba a la derecha, sin tapar la barra de tareas.
6. **Scraping**: pegá `https://www.youtube.com/@AgerGames/videos`: se identifica solo y muestra la tarjeta del canal, **sin lista de videos**. Elegí **Últimos 7 días**, **Buscar videos**: solo tienen que salir videos de esos días. Hacé clic en una fila: se estira con miniatura y descripción.
7. **Scraping**: abajo, **Reiniciar** pregunta antes (hay videos marcados) y deja todo en el paso 1.
8. **Suscripciones**: Scraping > Suscripciones > Agregar: abajo, "La primera vez" tiene **Desde hoy** (marcada), **Desde hace 7 días** y **Desde esta fecha**. Dejá "Desde hoy", Aceptar y **Revisar ahora**: dice cuántos videos marcó como vistos y no baja nada.

## Qué no pude comprobar
Un monitor real con otra escala, dos monitores reales, y una revisión completa de suscripción contra un canal de verdad (solo con yt-dlp falso). El .exe no lo abrí.
