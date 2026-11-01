# Ronda 1 de 0.3.1 - Scraping (rama feat/v0.3.1-scraping)

Spec: `docs/V0_3_1_SPEC.md` (secciones 0 y RONDA 1). El informe de la ronda de eficiencia anterior pasó a `docs/informes/ronda-0-eficiencia.md`.

## Bug de fechas (1.1)
- **Causa comprobada** (la hipótesis del spec era cierta): `Filters.Apply` dejaba pasar las entradas **sin fecha** (Shorts) y las de **fecha aproximada**
  (margen de 1 a 366 días); el paso de fecha exacta tenía tope de 100 y lo que quedaba sin confirmar seguía en la tabla.
- La prueba que lo reproduce se escribió primero y **falló antes del arreglo** (3 de 6 filas sin fecha confirmada: `edge-from`, `just-before` y el Short).
- Regla nueva (`Filters.Classify` / `Evaluate`): se muestra solo con fecha confirmada y dentro del rango, extremos incluidos. Lo dudoso (sin fecha, o aproximada
  cerca de un borde) **no se mezcla**: va aparte ("N videos sin fecha confirmada", "Ver cuáles", desmarcados). Lo claramente fuera del rango se descarta sin pedir nada.
- Los atajos (24 h / 7 días / este mes) solo llenan las dos fechas: misma regla. `Filters.Apply` (usado por Suscripciones) usa también la regla estricta;
  `CheckRunner` solo se tocó para que siga confirmando fechas dudosas. El resto de Suscripciones es de la ronda 3.

## Lo hecho
- **Motor** (`Videoprep.Core/Scraping`): `ScrapeSearch` (nuevo) = un listado por pestaña de lo nuevo a lo viejo con **corte temprano** propio, fecha exacta solo para
  dudosas con **tope de 150** (después pregunta "Faltan confirmar N fechas"), **caché de sesión** (10 min) con refiltrado local, avisos de 50 en sus dos variantes,
  estimación de pedidos calculada. `SourceLister`: `ListTargetAsync` (puede cortarse y seguir en la entrada N) y `ProbeAsync` (un pedido liviano, `-I 0`).
- **Pantalla**: tres pasos (enlace con tarjeta de canal, filtros con atajos y frase "Se va a buscar / Pedidos estimados", resultados con encabezado,
  "Fechas confirmadas A de B", barra y Cancelar). Filas expandibles con la **misma ficha de la Cola** (`VideoCard`, extraída; la Cola ahora la usa también),
  una sola abierta, mismo manejo de clic y teclas. Barra inferior: grupo **Selección** (Todo / Nada / Invertir + contador) y **Reiniciar** rojo, con pregunta solo si hay
  búsqueda en curso o casillas marcadas. Diálogo de tres botones nuevo (`AskThree`).
- Textos es/en: 58 claves nuevas.

## Comprobado de verdad (VERIFICAR, anotado en docs/VERIFY.md; 3 pedidos de listado a @BlenderStudio)
- El canal **no informa su total** (`playlist_count` vacío): el aviso previo "este canal tiene unos N videos" solo sale cuando el sitio lo informa (listas, otros
  sitios); en canales de YouTube la búsqueda se frena sola a los 50. Es lo que el spec preveía para "no se conoce el total".
- `--break-match-filters` **no corta** el listado plano (leyó los 136 videos del canal): se descartó y el corte lo hace videoprep matando yt-dlp tras 2 videos seguidos más viejos que "desde".
- Las fechas aproximadas de videos de más de una semana vienen redondeadas al día 1 del mes: justifica el margen y la confirmación exacta.

## Pruebas
- Core: `DateRangeTests` (bug + bordes + tope), `SearchFlowTests` (corte temprano, 50 en sus variantes, total conocido, límite, caché, vencimiento, estimación, sin cookies,
  freno ante verificación, pedido liviano), `Filters`. App: `ScrapingFlowTests` (17: pasos, atajos, regla de fechas en pantalla, 50, Reiniciar con y sin pregunta,
  Selección, fila expandible, ventana real renderizada). Suite completa local (sin ffmpeg real): **555 + 223 pruebas, todas verdes**.
- Capturas (tema oscuro, español): `tests/Videoprep.App.Tests/bin/Release/net10.0/screenshots/v031r1-scraping-{1-enlace,2-filtros,3-resultados}.png`.

## Decisiones y cosas que NO quedaron como pide el spec
1. **Cookies (1.4.4)**: el listado nunca usa cookies salvo que Nico las haya elegido en Ajustes (ya era así). El **reintento automático con la cadena de cookies** cuando el sitio las exige
   **no se hizo**: leería cookies del navegador sin preguntar (regla de CLAUDE.md). Hoy, ante la verificación, la búsqueda se frena y lo dice. Si Nico quiere el reintento, hay que decidir cómo pedir permiso.
2. **Miniatura (1.4.6)**: viene con el pedido de la descripción de la fila abierta (servicio de la Cola), no de una URL del listado; sigue siendo solo de la fila abierta y de a una.
3. **Pestañas**: además de Videos / Shorts / Directos dejé **Listas de reproducción** (la función ya existía; el spec no la menciona).
4. **Suscripciones**: "Suscribirme a esto..." copia las fechas escritas (un atajo como "Últimos 7 días" queda como fechas fijas); se revisa en la ronda 3.
5. Con la ventana chica, la tabla de resultados queda baja si la fila abierta es alta; no se ajustó el alto de las tarjetas de arriba.
6. Pedidos de confirmación: cada video es un pedido (se lanzan de a 10 por llamada con `--sleep-requests 1`); la estimación con fechas cuenta el tope de 150 como máximo.

## Pruebas inestables
Una prueba de pantalla (`ExtrasScreensTests.Queue_dialog_and_subscriptions_with_profiles`, en/gray) falló **una vez** en una corrida completa temprana y no volvió a fallar en 2 corridas completas
y 3 de su clase. No pude capturar el mensaje. Sin causa conocida y sin reproducir; si aparece en el CI se investiga (regla 8), no se reintenta.

## Pendiente
- **Prueba real de listado del canal Sparkles** (2026-10-23 a 2026-10-27, solo listar, sin descargar): falta el enlace de Nico.
- Un solo push y una corrida de CI para esta ronda; sin tag ni release.
