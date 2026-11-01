# videoprep 0.3.1 + sitio web — especificación

Documento de referencia para Claude Code. Lo escribió Claude (chat) junto con Nico. Los mockups de `docs/mockups-v0.3.1/` son la **referencia visual**; si un mockup contradice este texto, **manda este texto**. Todos los textos de la interfaz van en español e inglés (archivos de recursos existentes).

Nico no es técnico: los informes para él van en lenguaje simple (bloque "Para Nico", ver reglas en CLAUDE.md).

## 0. Reglas generales

- Solo Windows (regla ya en CLAUDE.md). No agregar nada para Mac/Linux, pero no cerrarles la puerta (usar `Platform`).
- Una rama por ronda, creada desde `main`. Un solo push al final de cada ronda. Reglas de trabajo de CLAUDE.md vigentes (una corrida de CI, sin plan previo si el spec ya define el pedido, capturas y .exe solo donde lo indica este spec, etc.).
- Al terminar cada ronda con CI en verde: mergear a `main` **sin tag ni release**. La versión sigue como "Sin publicar (0.3.1)" en el CHANGELOG. Publicar una versión solo cuando Nico diga "publicá".
- No tocar: cobros, precios, licencias comerciales, firma del .exe, macOS/Linux, opciones VBR/CBR (descartadas por ahora; "Velocidad constante" se mantiene tal cual).
- Si algo del código actual contradice este spec o hay una decisión que no está acá, **parar y preguntarle a Nico** con una pregunta concreta y dos opciones. No inventar.
- Lo marcado **VERIFICAR** debe comprobarse con una prueba o con un pedido real mínimo y anotarse en `docs/VERIFY.md` antes de apoyarse en eso.

## RONDA 1 — Scraping (rama `feat/v0.3.1-scraping`)

Referencias: `scraping-flujo.html`, `scraping-barra.html`, `cola-expandible.html`.

### 1.1 Bug del filtro de fechas (primero, antes que todo lo demás)

Síntoma reportado por Nico: en el canal Sparkles, con filtro de fechas 2026-10-23 a 2026-10-27, aparecen muchos videos fuera del rango. Hipótesis (no asumirla, comprobarla): entradas sin fecha o con fecha aproximada pasan el filtro.

1. Escribir primero una prueba que reproduzca el bug, con un yt-dlp falso (`IProcessRunner` fake) que devuelva: videos con fecha confirmada dentro del rango, confirmada fuera, justo en los dos bordes, sin fecha y con fecha aproximada. La prueba debe fallar antes del arreglo.
2. Regla nueva y estricta: **un video solo se muestra si su fecha está confirmada y cae dentro del rango, extremos incluidos**. Los de fecha sin confirmar **no se mezclan** con los resultados: se cuentan aparte y se avisa ("2 videos sin fecha confirmada" con un enlace "Ver cuáles" que los lista desmarcados y separados).
3. La misma regla vale para "Últimas 24 h / 7 días / Este mes".

### 1.2 Flujo en tres pasos (ver `scraping-flujo.html`)

**Paso 1 — Enlace.** Al pegar un enlace de canal o lista se hace sola la carga: **un único pedido liviano** que identifica qué es y muestra tarjeta con nombre, usuario y avatar, **sin traer ningún video** (la tabla no existe todavía). Botón "Cargar" por si hace falta repetir. Pestañas del canal a revisar: Videos (marcada), Shorts, Directos.

**Paso 2 — Filtros.**
- Atajos: Últimas 24 h, Últimos 7 días, Este mes, Entre fechas (dos campos de fecha).
- "Mostrar hasta N videos": **vacío = todos** (sin tope duro).
- Orden: más nuevos primero (por defecto) / más viejos primero.
- Casillas: omitir directos y exclusivos de miembros; omitir ya descargados (según el historial/archivo que la app ya lleva).
- "Más filtros" (plegado): duración mínima/máxima, título incluye/excluye.
- Frase resumen: "Se va a buscar: …" y "Pedidos estimados: X a Y". Enter en cualquier filtro busca.

**Paso 3 — Resultados.** Botón "Buscar videos". Recién ahí se llena la tabla, solo con lo que pasa los filtros. Arriba: "N videos pasan los filtros (se revisaron M)", "Fechas confirmadas: A de B", barra y botón Cancelar. Lo ya leído queda aunque se cancele.

### 1.3 Aviso de más de 50 videos

- Si no hay límite en "Mostrar hasta" y el resultado esperado supera 50, avisar **al apretar "Buscar videos"** cuando se pueda saber el total de antemano (sin filtro de fechas y si el sitio informa el total del canal — **VERIFICAR** si lo informa). Texto: "Este canal tiene unos N videos. Buscarlos todos son unos X pedidos de listado." Botones: Buscar todos / Limitar a 50 / Cancelar.
- Si no se conoce el total, o hay filtro de fechas: la búsqueda arranca y **se frena sola al llegar a 50**: "Ya van 50 videos y siguen apareciendo. ¿Seguir buscando?" Botones: Seguir / Dejar hasta acá / Cancelar. Se pregunta una sola vez por búsqueda (después de "Seguir", no vuelve a frenar).
- Si hay 50 o menos, no aparece ningún aviso.

### 1.4 Cuidado de los servidores (obligatorio)

1. **Un único listado por pestaña**, leído del más nuevo al más viejo, con **corte temprano** al llegar a un video anterior a la fecha "desde" (usar `--lazy-playlist`/`--break-match-filters` o equivalente; la fecha aproximada con `--extractor-args youtubetab:approximate_date` ya se verificó en `docs/VERIFY.md`: revalidar con una prueba).
2. **Fecha exacta solo cuando hace falta**: solo para entradas dudosas o cercanas al borde del rango. **Tope de 150 pedidos** de confirmación por búsqueda; si se llega, preguntar antes de seguir ("Faltan confirmar N fechas. ¿Seguir?").
3. **Caché de sesión** (unos minutos) de lo ya leído, con refiltrado local: cambiar filtros no repite pedidos; solo se pide de nuevo si se amplía el rango hacia fechas más viejas que lo ya leído.
4. **Sin cookies en canales públicos**, salvo que el sitio las exija (reintento con la cadena de cookies existente).
5. Pedidos **de a uno**, con pausas, y el freno automático existente ante 429 / "no sos un bot".
6. Miniaturas: solo de la fila abierta, desde la URL estática que traiga el listado. Descripción: pedida **recién al abrir la fila**, de a una, y guardada mientras el programa esté abierto.
7. La frase "Pedidos estimados" debe reflejar estas reglas (no inventar números: calcular a partir de filtros y de lo ya conocido).

### 1.5 Resultados: filas expandibles y barra inferior

- Cada fila se abre estirándose (misma ficha que la Cola: miniatura, título, canal, fecha, vistas, descripción, enlace, "Se descargará"). **Extraer y reutilizar el componente de ficha de la Cola**; no duplicar código. Una sola fila abierta a la vez; mismo comportamiento de teclado que en la Cola.
- Barra inferior (ver `scraping-barra.html`): grupo con título **"Selección"** que contiene **Todo / Nada / Invertir** y el contador "2 de 6"; al lado, botón **rojo "Reiniciar"**. A la derecha: Perfil, **Enviar a la cola**, **Suscribirme a esto...** (como hoy).
- **Reiniciar** borra tabla, enlace, filtros y fila abierta, y vuelve al paso 1. Pregunta antes **solo** si hay una búsqueda en curso o casillas marcadas ("¿Reiniciar la búsqueda?").

### 1.6 Pruebas de la ronda 1

- La del bug de fechas (primero, debe fallar y después pasar).
- Filtros (fechas con bordes, duración, título, omitir directos/ya descargados), corte temprano, tope de 150, aviso de 50 en sus dos variantes, caché/refiltrado, Reiniciar (con y sin confirmación), selección Todo/Nada/Invertir.
- Pruebas de UI headless de los tres pasos y de la fila expandible.
- **Prueba real, solo de listado**: al final de la ronda pedirle a Nico la URL del canal Sparkles y listar sin descargar nada, con el rango 2026-10-23 → 2026-10-27; verificar que ningún resultado queda fuera del rango. Si Nico no la tiene, saltear y avisar.
- Capturas: una sola, tema oscuro y español, de cada pantalla cambiada (regla 3).

## RONDA 2 — Prep, pestañas y modo mini (rama `feat/v0.3.1-prep-mini`)

Referencias: `prep-compacto.html`, `pestanas.html`, `modo-mini-niveles.html`.

### 2.1 Pestañas

- La pestaña "Descargar" pasa a llamarse **Prep**. Pestañas: **Prep, Cola, Scraping, Ajustes, Registro** (en inglés: Prep, Queue, Scraping, Settings, Log). Se mantiene cómo está hoy cada lugar de Suscripciones (Claude Code lo confirma antes en la ronda 3).
- Cada pestaña lleva un ícono chico antes del nombre (estilo del mockup, trazo simple, mismo color que el texto).
- Tooltip de Prep: "Preparar y descargar un video" / "Prepare and download a video".
- Botón de la barra de pestañas, arriba a la derecha del todo: **"Modo mini" / "Mini mode"** con ícono de ventana chica.

### 2.2 Pestaña Prep compacta (ver `prep-compacto.html`)

- Barra de enlace con botón **Pegar**.
- Bloque Perfil/Formato a la mitad de alto: **Perfil** primero (desplegable + "Guardar como perfil..."), Tipo como interruptor **Video / Solo audio**, Formato, Resolución máx. y **Velocidad constante** (sin cambios de comportamiento). La descripción larga pasa a un ícono "i" con tooltip.
- **Vista previa grande** a la derecha, a lo alto de Perfil + Fragmentos (miniatura 16:9 con duración, título y canal).
- **Fragmentos**: encabezado con "+ Agregar fragmento"; vacío ocupa poco ("Video completo…") y crece al agregar. Cada fragmento tiene inicio, fin y **su propia caja de nombre**. Caja vacía → el archivo se guarda como "Título original - parte N".
- **Nombre del archivo**: un solo desplegable (Título original / Título personalizado / Serie) con su campo al lado. **Si hay al menos un fragmento, esta sección se deshabilita** (atenuada, con el aviso "Con fragmentos, cada uno lleva su propio nombre") y se reactiva al quitar todos.
- Abajo: carpeta de destino en una línea (+ Examinar y abrir carpeta); botones **Opciones..., Enviar a la cola, Descargar, Detener** juntos a la derecha; barra de progreso y estado. Quitar el botón "Mini" de esta zona.
- La ventana puede ser más baja que ahora; si se agranda, el espacio extra lo toma Fragmentos.

### 2.3 Modo mini en tres niveles (ver `modo-mini-niveles.html`)

- **Maxi** = ventana completa. **Mini** = la ficha (≈340 px de ancho): botón ⤢ (a Maxi), título, botón **▁ modo micro** al lado del **pin** (el pin se ve en rojo cuando "siempre encima" está activo), campo de enlace + Pegar, perfil, casilla **Fragmento** (que despliega inicio y fin), **A la cola**, **Descargar**, estado y barra de progreso. **Micro** = barra de una línea (≈380×40): ⤢ (a Mini), texto de estado, Detener cuando baja, y barra de progreso fina.
- Texto de Micro en reposo: **"Copiá un enlace para descargar"** / "Copy a link to download". Mientras descarga: título + porcentaje y velocidad + Detener.
- Transiciones: **doble clic** en Micro → Mini; doble clic en Mini → Maxi; botón ▁ en Mini → Micro; ⤢ sube un nivel. El doble clic solo vale sobre zonas vacías o textos, **no** sobre botones, campos ni desplegables.
- Se arrastra desde cualquier zona vacía. Siempre encima de otras ventanas (como hoy).
- **Posición (cambia lo actual):** al pasar de Maxi a Mini o Micro, la ventana nueva aparece **en el mismo monitor que la ventana completa y pegada a su esquina superior derecha** (dentro del área de trabajo del monitor, sin pisar la barra de tareas; considerar escala/DPI de cada monitor). Los cambios entre Micro/Mini mantienen esa esquina superior derecha. Al volver a Maxi, la ventana completa recupera su tamaño y posición originales. Ya **no** se recuerda la última posición de la mini entre usos. Si el monitor guardado ya no existe, usar el de la ventana completa. Si la ventana completa estaba maximizada: esquina superior derecha de su monitor.
- Mantener lo ya existente: Esc, el menú, y la persistencia de "siempre encima".

### 2.4 Pruebas de la ronda 2

- Fragmentos con nombre propio (vacío y con texto), deshabilitado/rehabilitado del nombre del archivo, perfiles.
- Cambio de nivel Maxi/Mini/Micro y doble clic (sin disparar sobre controles).
- **Posición con dos monitores simulados** (abstracción `Screens` falsa): monitor distinto, escala distinta, ventana maximizada, límites del área de trabajo, restauración.
- Textos es/en de todo lo nuevo. Una captura en oscuro/español de Prep, Mini y Micro.

## RONDA 3 — Suscripciones y cierre (rama `feat/v0.3.1-subs-cierre`)

### 3.1 Suscripciones

Primero **leer cómo está hoy** (se supone que vive dentro de Scraping, con "Suscribirme a esto..."; confirmarlo y avisar si es distinto). Aplicar, donde corresponda y reutilizando los mismos componentes que Scraping:

- Los **mismos filtros** al crear/editar una suscripción (fechas, duración, título, omitir directos/miembros) con el mismo código; **misma regla estricta de fechas** y mismo aviso de "sin fecha confirmada".
- **Filas expandibles** (ficha de la Cola) en cualquier lista de videos de esa zona; grupo **Selección** donde haya listas con casillas.
- Avisos de más de 50 videos y tope de 150 pedidos, igual que en Scraping.
- **Sin botón Reiniciar**: lo equivalente es "Quitar suscripción", con confirmación.
- **Menos carga**: cada revisión lee de lo más nuevo hacia atrás y se detiene al toparse con un video ya registrado en su archivo de descargados (`--break-on-existing` o equivalente); espaciar los pedidos entre suscripciones; no revisar todas a la vez.
- **Primera revisión de una suscripción nueva**: no baja el historial completo. Elegir desde cuándo: "Desde hoy" (por defecto), "Últimos 7 días" o una fecha.

### 3.2 Cierre de la versión

- Capturas de pantalla de **todo lo cambiado en 0.3.1**, en los **6 temas × 2 idiomas**, en `docs/capturas/0.3.1/`.
- Generar el **.exe** en una carpeta versionada (como siempre) para que Nico lo pruebe.
- CHANGELOG ("Sin publicar (0.3.1)") completo, `docs/VERIFY.md` al día, `docs/ARQUITECTURA.md` actualizado, informe en `docs/informes/`.
- Bloque "Para Nico": checklist de **8 pasos como máximo** para probar lo nuevo (Scraping, Prep, mini en dos monitores si tiene, Suscripciones).
- No publicar. Esperar el "publicá" de Nico.

## SITIO WEB (rama `feat/sitio-web`, después de publicar la 0.3.1)

Referencia: `pagina-web.html` (es la página completa, con la demostración clickeable). **Nada de cobros, precios, planes, donaciones, "Pro" ni suscripciones**, ni en la página ni en los textos legales.

1. Crear `site/index.html` a partir del mockup: **un solo archivo HTML**, editable a mano por Nico, con español/inglés (`data-es` / `data-en`) y los colores en `:root`. Mantener los comentarios que marquen secciones.
2. Enlaces reales: botón de descarga → `https://github.com/nicolasrabe/videoprep-next/releases/latest`; "Ver en GitHub" y "Código en GitHub" → el repositorio; "Reportar un problema" → `/issues`. La versión mostrada se obtiene del último release de GitHub con JavaScript, con texto de respaldo si falla.
3. **Verificar cada afirmación de la página contra la app real** y corregir el texto si no coincide: "sin instalador", "no recopila datos de uso", "verifica que el archivo no esté dañado" (SHA-256), lista de formatos, qué hace Scraping, etc. Hacer una **auditoría de las conexiones de red** del programa (qué pedidos hace y a dónde: sitios de video por yt-dlp, GitHub para buscar actualizaciones) y escribir `site/privacidad.html` (es/en) **solo con lo que sea verdad**. Si hay algo que contradiga "no recopila datos", avisar a Nico antes de publicar.
4. La demostración clickeable debe reflejar la interfaz **ya publicada** (0.3.1): ajustar textos o pantallas del mockup que no coincidan.
5. `.github/workflows/pages.yml`: despliega `site/` con las acciones oficiales de GitHub Pages cuando cambie `site/**` en `main` (y a mano). **No habilitar Pages ni cambiar configuración del repositorio**: eso lo hace Nico (en el informe, decirle exactamente dónde: Settings → Pages → Source: GitHub Actions; y que el repositorio debe ser público).
6. Accesibilidad básica y celular (revisar a 380 px de ancho), título y descripción para buscadores, y `site/README.md` de 10 líneas que explique en simple cómo editar la página y cómo se publica.
7. Probar la página localmente con el navegador instalado de Playwright (pestañas, cola, modo mini, cambio de idioma, 380 px y 1100 px) y adjuntar capturas en el informe.
