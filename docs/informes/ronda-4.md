# Ronda 4 de 0.3.1 - Ajustes de uso (rama feat/v0.3.1-ajustes)

Spec: `docs/V0_3_1_SPEC.md`, bloque RONDA 4 (agregado en esta ronda). La ronda 3 ya estaba en `main` (`0b95303`). El mockup `mockup-cambios-ronda4.html` no está en el repositorio: se siguió el texto del pedido.

## Hecho
- **A, monitor del modo mini**: `MiniPlacement.FullFromMini` + `MiniPlacer.Close`. La ventana completa vuelve al monitor donde está la mini, con su tamaño (en la escala de ese monitor) y su esquina superior derecha donde estaba la mini; nunca fuera del área de trabajo ni más grande que ella. Regla de 2.3 actualizada en el spec; 6 pruebas (núcleo y app).
- **B, Scraping**: `CollapsibleSection` en los pasos 1, 2 y 3 (enlace plegado deja avatar + nombre + @; filtros plegado deja el resumen; resultados `Grows`). "Buscar videos" pliega Filtros; Reiniciar lo abre todo. Placeholder nuevo, texto largo fuera. Ajustes: 8 grupos plegables (3 plegados de entrada). Registro: barra arriba, lista ocupa el resto.
- **C, miniaturas**: `ThumbnailService` (maxres→sd→hq, mejor de los metadatos en otros sitios, caché memoria + disco 200 MB). `VideoInfoService`: caché de datos 24 h en disco, miniatura de YouTube en paralelo, prefetch de la fila siguiente (1 a la vez, solo si está libre, nunca tras un fallo). `LoadingText` en Prep, ficha (miniatura y descripción aparte), carga del canal, resultados.
- **D, Descargar miniatura...**: en Cola, Scraping y vista previa de Prep. "Guardar como" se abre antes de buscar la imagen. Mismo formato = mismos bytes; si no, ffmpeg de la app (JPG `-q:v 2`, PNG sin pérdida).
- **E, Cola**: barra Iniciar cola / Pausar / Detener + contador (mismos comandos que Prep). Pausar corta entre videos (`EngineContext.PauseRequested`). Arrastre por el agarre de "#" con línea de destino (`QueueOrder`, guardado en la cola, un Ctrl+Z lo deshace).
- **F, Suscripciones**: menús de clic derecho (sobre una suscripción y sobre el espacio vacío), es/en.

## Medición (3 videos reales de YouTube, primera vez, sin caché)
Abrir una ficha completa: **4,6 s antes, 4,2 s ahora**. La miniatura aparece en ~0,2 s en lugar de esperar al final; la segunda vez sale del disco.
Prueba única pedida: `youtube:skip=hls,dash,translated_subs` en el pedido de la ficha: mismos campos, 3,9 s contra 4,1 s. Quedó puesto.

## Pruebas
Núcleo: 601 verdes (3 corridas seguidas); app: 233 verdes; nuevas: miniaturas (5), exportar (4, una con ffmpeg real), caché de fichas (3), orden y pausa de la cola (9), monitor (4), argumentos (1). App: componentes (3), cola, miniatura, menús de suscripciones (9), Scraping plegable (3). La prueba de textos es/en pasa.
Capturas (oscuro/es, 8 pantallas): `docs/capturas/0.3.1/ronda-4/`. .exe: `dist\win-x64-0.3.1-prueba2\videoprep.exe`.

## Lo que no se pudo comprobar
- El arrastre real con el mouse (las pruebas ejercitan el cálculo del destino y el guardado, no el gesto).
- Los menús de clic derecho abiertos de verdad, la ventana completa volviendo a otro monitor real, el .exe abierto (no lo abrí).
- Prefetch contra un sitio real (se probó con yt-dlp falso).
