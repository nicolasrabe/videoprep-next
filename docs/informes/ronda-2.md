# Ronda 2 de 0.3.1 - Prep, pestañas y modo mini (rama feat/v0.3.1-prep-mini)

Spec: `docs/V0_3_1_SPEC.md` (secciones 0 y RONDA 2). Capturas (tema oscuro, español) en `tests/Videoprep.App.Tests/bin/Release/net10.0/screenshots/`:
`v031r2-prep-sin-fragmentos.png`, `v031r2-prep-con-fragmentos.png`, `v031r2-mini-ficha.png`, `v031r2-mini-micro.png`.

## Pedidos extra de Nico
1. **Prueba de textos sin pantallas** (`tests/Videoprep.Core.Tests/TextFilesTests.cs`): lee directamente `es.json`, `en.json` y los archivos `.axaml`/`.cs`.
   Revisa: ninguna clave repetida dentro de un archivo, mismas claves en español e inglés, ningún texto vacío, toda clave que piden las pantallas (`{s:T clave}`) y el código (`L["clave"]`, `L.Format("clave"...)`) existe, y
   las pantallas **no tienen texto fijo sin traducir** (atributos Content/Text/Header/Title/PlaceholderText/ToolTip/Name y texto entre etiquetas; solo se permiten `videoprep`, `i`, `X`, `Ctrl+Z`, `M:SS`, `H:MM:SS` y colores).
   Comprobé que detecta: metí a propósito una clave inexistente en una pantalla, otra en el código y dos textos fijos, y las 3 pruebas fallaron con el detalle; después lo saqué. No usa Avalonia ni ventanas: no puede fallar al azar.
   Recupera la protección que se perdió al marcar como `Inestable` las pruebas de pantalla (Cola, mini, Ajustes).
2. **Sin aplicación de pruebas compartida**: las pruebas de pantalla nuevas usan el mismo mecanismo que las que andan bien (una aplicación por prueba, `[AvaloniaFact]`). No se tocó `TestAppBuilder`.

## Lo hecho
- **Pestañas (2.1)**: "Descargar" ahora es **Prep** (tooltip "Preparar y descargar un video" / "Prepare and download a video"); cada pestaña lleva un ícono de trazo simple del color del texto (los del mockup `pestanas.html`);
  arriba a la derecha de la barra de pestañas el botón **Modo mini / Mini mode** con ícono de ventana chica (salió de la pestaña Prep). Ventana un poco más baja (alto mínimo 560).
- **Prep compacta (2.2)**: botón **Pegar** junto al enlace; **Perfil** primero (+ "Guardar como perfil...") y la descripción del formato pasó a un ícono **"i"** con tooltip; **Video / Solo audio** como interruptor; Formato, **Máx.** y Velocidad constante en la segunda línea;
  **vista previa grande** a la derecha (miniatura 16:9 con la duración encima, título y canal); **Fragmentos**: encabezado con "+ Agregar fragmento", vacío ocupa poco ("Video completo") y crece; cada fragmento tiene inicio, fin y **su propia caja de nombre**
  (vacía = "Título original - parte N", en inglés "part N"); **Nombre del archivo**: un solo desplegable (Título original / personalizado / Serie) con su campo; **con al menos un fragmento se apaga** y avisa "Con fragmentos, cada uno lleva su propio nombre", y se reactiva al quitarlos todos
  (lo escrito queda); carpeta en una línea; Opciones / Enviar a la cola / Descargar / Detener juntos a la derecha. El espacio extra de la ventana lo toma Fragmentos.
- **Nombres de fragmentos (motor)**: `ClipRange` ahora tiene `Name`; el elemento de la cola guarda `PartWord` ("parte"/"part" según el idioma al encolar). El nombre del fragmento gana sobre cualquier otro; los sin nombre salen "Título - parte N".
  Los elementos viejos de la cola (sin `PartWord`) siguen con "Título - N": comprobado que las colas guardadas antes se cargan. Con fragmentos y nombre propio, el archivo completo descargado sigue llamándose por el título (para armar los "parte N").
- **Modo mini en tres niveles (2.3)**: **Maxi** (ventana completa), **Mini** (ficha de 340 px: ⤢, título, ▁ micro, pin que se ve rojo cuando "siempre encima" está activo, enlace + Pegar, perfil, casilla **Fragmento** que abre inicio y fin, A la cola, Descargar, estado con porcentaje y velocidad, barra de progreso)
  y **Micro** (barra de 380×40: ⤢, texto de estado, **Detener** solo mientras baja, barra fina). Micro en reposo: **"Copiá un enlace para descargar"**. Doble clic en Micro → Mini, en Mini → Maxi; ▁ en Mini → Micro; ⤢ sube un nivel. El doble clic **no vale sobre botones, cajas ni desplegables**.
  Se arrastra desde cualquier zona vacía (el arrastre empieza al mover 4 px, para que el clic y el doble clic sigan siendo clics). La ficha puede tomar el teclado (tiene una caja de texto); la barra no (como el mini de antes). Siempre encima, Esc y el menú siguen.
- **Posición (cambia lo anterior)**: de Maxi a Mini/Micro la ventana nueva aparece **en el mismo monitor que la ventana completa, pegada a su esquina superior derecha**, dentro del área de trabajo (no pisa la barra de tareas) y con la escala de **ese** monitor. Entre Micro y Mini se mantiene esa esquina (aunque la hayas arrastrado a otro monitor).
  Al volver a Maxi la ventana completa recupera tamaño y posición (y maximizada si estaba). **Ya no se recuerda la última posición** (se quitaron `miniX`/`miniY` de los ajustes; los archivos viejos con esos campos se siguen leyendo). Si el monitor ya no existe, se usa el principal.
  La lógica está separada de las ventanas (`MiniPlacement` en el núcleo y `MiniPlacer` en la app con `IScreenSource`/`IMiniSurfaces`) para poder probarla con monitores falsos.
- Textos nuevos es/en: ~30 claves.

## Pruebas
- Núcleo: `TextFilesTests` (9), `MiniPlacementTests` (7: dos monitores con escala distinta, ventana entre dos monitores, maximizada, barra de tareas arriba/izquierda, mantener la esquina, esquina fuera de pantalla, monitor que ya no está, sin monitores),
  `FragmentNameTests` (4, incluye colas viejas y JSON), motor con ffmpeg real `Fragments_are_saved_with_their_own_name_or_Titulo_original_parte_N` (el archivo sale "Entrada.mp4", "<título> - parte 2.mp4", "Final.mp4").
- App: `MiniPlacerTests` (7, monitores y ventanas falsos: monitor distinto, escala distinta, maximizada, restauración, no recuerda posición, monitor que ya no está, barra de tareas), `MiniLevelsAndPrepTests` (niveles, casilla Fragmento, Pegar, interruptor Video/Audio, desplegable de nombre, nombres que se apagan, envío con nombres, inglés),
  `PrepAndMiniScreensTests` (pestañas con ícono en es/en, tooltip, botón de modo mini a la derecha, Prep compacta, ficha, barra, doble clic **real** por la entrada simulada de Avalonia sobre texto vs. caja/lista/casilla).
- Se actualizaron las pruebas viejas que asumían el comportamiento anterior (pestaña "Descargar", ejemplo de nombres con fragmentos, "Todo listo" en reposo, posición guardada, alto mínimo).
- Suite normal local (sin ffmpeg real): **569 (núcleo) + 211 (app)**, todas verdes. La prueba de `Mini_window_in_its_states` (12 combinaciones, una de las marcadas `Inestable`) **se borró**: probaba el mini de una línea que ya no existe; la reemplazan las de arriba, que no fallan al azar.
  Las otras dos `Inestable` siguen a mano (24 casos, pasan).

## Decisiones y lo que NO quedó como pide el spec
1. La **ficha mini deja de ser "sin foco"**: tiene una caja de enlace donde hay que escribir, así que puede activarse; la barra micro sigue sin tomar el teclado.
2. En la ficha, desmarcar **Fragmento** borra los fragmentos del borrador (la casilla refleja el borrador compartido con la pestaña Prep).
3. **Cambiar de idioma no cambia** el "parte" de elementos ya encolados (queda el que había al encolar).
4. Con el **mouse y el doble clic** no pude probar un monitor real ni otra escala: la lógica está probada con monitores falsos; en pantalla real solo se vio una ventana de pruebas. Falta que Nico lo mire con dos monitores (checklist en la ronda 3).
5. La velocidad en la ficha sale del texto que da yt-dlp (por ejemplo "7.16MiB/s"); no la convertí al formato de la app.
6. Los íconos son trazos propios copiados del mockup (sin librería de íconos).

## Pendiente
- Un solo push y una corrida de CI; merge a main sin tag. Ronda 3 (Suscripciones y cierre) después.
