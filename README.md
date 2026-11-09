# OffersFinder

Aplicación de consola en **.NET 10** que vigila los precios de los productos que quieres comprar, en tiendas que **envían a España o permiten recoger en la Comunidad de Madrid**, y te avisa por Telegram (o email o WhatsApp) cuando el precio baja o alcanza tu objetivo. Guarda el histórico de cada consulta y genera un informe con **gráficas, mínimo, máximo, media y mediana**, filtrable por mes. Se ejecuta sola cada cierto tiempo con GitHub Actions (o en bucle en tu propio equipo).

## Qué hace

- **Dos formas de vigilar un producto**
  - Por **búsqueda de texto**: consulta **Google Shopping (España)** a través de la API de Serper.dev, que devuelve en una sola llamada los precios de la mayoría de tiendas españolas; filtra los resultados por palabras clave, exclusiones y rango de precio y se queda con la oferta más barata. Sin clave de Serper, consulta directamente a cada tienda del catálogo (`stores.json`), aunque casi todas bloquean el acceso automático desde servidores.
  - Por **URL concreta**: lee el precio de la página del producto (JSON-LD, metaetiquetas, `itemprop`, clases `price` o un selector CSS que indiques).
- **Tiendas**: el catálogo marca para cada tienda si envía a España y si tiene recogida en la Comunidad de Madrid. Con `pickupMadridOnly` un producto solo acepta ofertas de tiendas con recogida en Madrid.
- **Avisos por Telegram**: bajada respecto al último precio (con porcentaje mínimo configurable), precio objetivo alcanzado y precio más bajo registrado. Cada aviso incluye las mejores ofertas con tienda, precio, enlace y si hay recogida en Madrid. Email (un solo correo HTML por ejecución) y WhatsApp son opcionales.
- **Histórico y estadísticas**: `history` muestra mínimo, máximo, media, mediana y variación, con filtro `--month AAAA-MM`, y el último precio y mínimo de cada tienda. Se guarda el precio de cada tienda en cada consulta, no solo el mejor.
- **Resumen diario e informe semanal**: tras cada comprobación llega por Telegram el estado de cada producto (precio, variación, mínimo, objetivo y precio por tienda) y la gráfica de cada producto como imagen, aunque no haya bajadas; cada lunes (variable `REPORT_DAY`) llega además el informe HTML completo como archivo.
- **Emparejado tolerante**: "2 TB" y "2tb", "MX-12" y "mx 12" o acentos y mayúsculas se consideran iguales; `mustContain` y `exclude` admiten sinónimos con `|` ("2tb|2000gb").
- **Gastos de envío**: Google Shopping indica el envío de cada oferta ("Envío gratis", "Envío 4,99 €"); se muestra en avisos, resumen e informe y, con `compareWithShipping`, se suma al precio para comparar. Las fichas leídas directamente solo lo aportan si lo declaran en sus datos estructurados.
- **Sin stock**: las fichas que declaran el producto agotado (schema.org, metaetiquetas o el texto de stock) se descartan.
- **Informe con gráficas**: `report` genera `data/report.html`, un HTML estático sin JavaScript (se ve en cualquier visor, móvil incluido) con una gráfica SVG por producto (mejor precio, una línea por tienda y objetivo), estadísticas, pestañas por mes, tabla por tienda y tabla de mejores ofertas.
- **Avisos destacados**: el aviso de precio objetivo alcanzado se fija en el chat de Telegram para que quede arriba del todo.
- **Gestión**: `remove` elimina un producto (y su histórico), `blacklist` veta una tienda para un producto o para todos.

## Requisitos

- [.NET SDK 10.0](https://dotnet.microsoft.com/download/dotnet/10.0)

## Puesta en marcha

```bash
git clone <este repositorio>
cd OffersFinder
cp .env.example .env        # pega el token de tu bot de Telegram (ver "Avisos")
dotnet build
dotnet run --project src/OffersFinder -- help
```

Para no escribir `dotnet run --project src/OffersFinder --` cada vez:

```bash
dotnet publish src/OffersFinder -c Release -o ./app
./app/OffersFinder help          # Linux/macOS
.\app\OffersFinder.exe help      # Windows
```

En los ejemplos siguientes se usa `offersfinder` como abreviatura de cualquiera de las dos formas.

## Añadir productos

Por línea de comandos:

```bash
# URL concreta
offersfinder add https://www.pccomponentes.com/samsung-990-pro-2tb --name "SSD 990 Pro 2TB" --target 150

# Búsqueda en todas las tiendas que envían a España
offersfinder add --query "Samsung 990 Pro 2TB" --name "SSD 990 Pro 2TB" --target 150 --min 80 --max 300 --exclude disipador,4tb

# Solo tiendas con recogida en la Comunidad de Madrid
offersfinder add --query "Nintendo Switch OLED" --target 280 --min 200 --max 400 --exclude funda,mando --pickup-madrid
```

O editando `watchlist.json` directamente:

```json
{
  "blacklistedStores": ["carrefour"],
  "items": [
    {
      "name": "SSD 990 Pro 2TB",
      "url": "https://www.pccomponentes.com/samsung-990-pro-2tb",
      "query": "Samsung 990 Pro 2TB",
      "mustContain": ["990", "2tb"],
      "exclude": ["disipador", "heatsink", "4tb"],
      "minPrice": 80,
      "maxPrice": 300,
      "targetPrice": 150,
      "pickupMadridOnly": false,
      "excludeStores": ["amazon_es"],
      "alertOnDrop": true
    }
  ]
}
```

| Campo | Significado |
|---|---|
| `url` | Página concreta del producto. Si la tienda está en la lista negra, se omite. |
| `urls` | Más fichas de producto (en distintas tiendas) que se leen directamente. No gastan consultas de la API. Además, el programa **aprende solo** las fichas con enlace directo que encuentra la API y las lee a diario; las que fallan tres veces seguidas o dejan de corresponder al producto se descartan. |
| `query` | Texto a buscar en el catálogo de tiendas. Puede combinarse con `url`; se usa el precio más bajo de ambos. |
| `mustContain` | Palabras que deben aparecer en el título del resultado (si falta, se usan las de `query`). |
| `exclude` | Palabras que descartan un resultado (accesorios, otras capacidades...). |
| `alsoAccept` | Conjuntos alternativos de palabras que también se aceptan, p. ej. `[["casco","micro"]]`, para títulos recortados de Google Shopping. Esas ofertas se marcan como "modelo sin confirmar"; conviene acompañarlo de `exclude` con los otros modelos. |
| `minPrice` / `maxPrice` | Rango plausible: evita que un accesorio barato o un pack caro se cuele como "mejor oferta". |
| `targetPrice` | Avisa cuando el precio sea igual o inferior. Se vuelve a avisar solo si baja más o si subió y volvió a bajar. |
| `pickupMadridOnly` | Solo tiendas con recogida en la Comunidad de Madrid. |
| `compareWithShipping` | Comparar, ordenar y medir el objetivo con el precio **más el envío** cuando se conoce. Por defecto `false`: se compara el precio del producto y el envío se muestra a título informativo. |
| `stores` | Limita la búsqueda a esos identificadores de tienda. |
| `excludeStores` | Lista negra de tiendas **para este producto**. |
| `selector` | Selector CSS del precio en la página de `url` (si la detección automática falla). |
| `alertOnDrop` | Avisar ante cualquier bajada respecto a la última consulta (por defecto `true`). |
| `headers` | Cabeceras HTTP extra (por ejemplo `Cookie`) para tiendas exigentes. |

## Comandos

| Comando | Qué hace |
|---|---|
| `check` | Comprueba todos los productos, guarda el histórico, regenera el informe y envía avisos. |
| `check --loop --every 6h` | Lo mismo, repitiendo cada intervalo (`30m`, `6h`, `1d`). |
| `check --only casco,3` | Comprueba solo los productos cuyo nombre contenga "casco" o tengan el número 3. En GitHub, la casilla **only_items** del flujo manual. |
| `check --notify-result` | Envía además un mensaje con el resultado de cada producto aunque no haya bajadas. Las ejecuciones manuales del flujo lo hacen siempre. |
| `check --dry-run` | Comprueba sin guardar ni enviar avisos externos. |
| `list` | Lista los productos con último precio, mínimo y objetivo. |
| `history [nombre] [--month 2026-11] [--all]` | Histórico con mínimo, máximo, media, mediana y variación. |
| `report [--open] [--output ruta.html]` | Genera el informe HTML con gráficas. `--open` lo abre en el navegador. |
| `stores` | Muestra el catálogo: envío a España, recogida en Madrid y lista negra. |
| `blacklist <idTienda>` | Lista negra **global**: la tienda no se consulta para ningún producto. |
| `blacklist <idTienda> --item <nombre\|número>` | Lista negra **solo para ese producto**. |
| `unblacklist <idTienda> [--item ...]` | Quita la tienda de la lista negra. |
| `remove <nombre\|URL\|número> [--keep-history]` | Elimina el producto y su histórico (salvo `--keep-history`). |
| `test-notify` | Envía un aviso de prueba a los canales configurados. |
| `telegram-setup` | Muestra tu `TELEGRAM_CHAT_ID` a partir del token del bot. |
| `notify "Título" "Texto"` | Envía un mensaje cualquiera por los canales configurados (lo usa el flujo para recordatorios). |
| `summary [--days 7]` | Resumen de los últimos días: precio actual, variación, mínimo, objetivo y precio por tienda. El flujo lo envía a diario. |
| `send-report [--images-only\|--file-only]` | Envía por Telegram una imagen con la gráfica de cada producto y el informe HTML completo como archivo. El flujo envía las imágenes a diario y el archivo los lunes. |
| `diagnose [nombre]` | Muestra qué ve el programa en cada tienda: tarjetas detectadas, títulos, precios y por qué un resultado no encaja. |

## Fuente de precios: Google Shopping

Las tiendas españolas (PcComponentes, MediaMarkt, El Corte Inglés, Fnac, Carrefour, Worten, Decathlon, Leroy Merlin, idealo y Amazon) protegen sus buscadores con Cloudflare, Akamai o captchas y rechazan las peticiones que llegan desde servidores (GitHub Actions, Azure, cualquier nube). Por eso la búsqueda se hace en **Google Shopping**, que ya agrega los precios de todas ellas, a través de [Serper.dev](https://serper.dev):

1. Crea una cuenta en https://serper.dev. Regala 2.500 consultas; después cuesta alrededor de 1 $ por cada 1.000. Cada producto consume dos consultas por comprobación (Shopping y web); dos productos una vez al día son unas 120 al mes.
2. Copia la clave de API y ponla en `.env` como `SERPER_API_KEY` (y como secreto `SERPER_API_KEY` en GitHub).

Alternativa con los mismos resultados: [SerpApi](https://serpapi.com), con un plan gratuito de unas 100 consultas al mes (da para la comprobación diaria de un producto con búsqueda web, o de tres con `WEB_SEARCH=false`) y planes de pago por encima. Su clave va en `SERPAPI_API_KEY`. Basta con una de las dos; si existen ambas, se usa Serper.

Cada resultado trae título, precio, tienda y enlace. El catálogo `stores.json` sirve para reconocer la tienda (`aliases`), saber si tiene recogida en Madrid y aplicar las listas negras. Las tiendas que no están en el catálogo se aceptan igualmente (Google Shopping España solo muestra tiendas que venden en España), salvo que el producto tenga `pickupMadridOnly`. Su identificador para las listas negras es su nombre en minúsculas y sin espacios ni acentos (por ejemplo `wallapop`); `watchlist.json` excluye de entrada los mercadillos de segunda mano `wallapop`, `milanuncios` y `ebay`.

**Búsqueda web además de Shopping.** Con la misma clave, el programa hace también una búsqueda normal en Google, lee las páginas de resultados (hasta `WEB_SEARCH_MAX_PAGES`, 10 por defecto) y añade el precio cuando la página lo declara de forma estructurada (JSON-LD, metaetiquetas o `itemprop`), que es lo habitual en Shopify, PrestaShop, WooCommerce y similares. Así entran tiendas pequeñas que no están en Shopping. Las páginas que no son tiendas (foros, YouTube, Wikipedia) y las tiendas que bloquean el acceso automático se omiten. Cuesta una consulta más por producto; desactívalo con `WEB_SEARCH=false` (en GitHub, la variable de repositorio `WEB_SEARCH`).

**Reservar consultas.** Con `API_PAUSE_UNTIL=AAAA-MM-DD` (en GitHub, la variable de repositorio del mismo nombre) la aplicación no llama a la API hasta esa fecha y solo lee las fichas de producto indicadas en `url` y `urls`. Útil para guardar el cupo gratuito para fechas como el Black Friday. Con `API_PAUSE_EXCEPT_WEEKDAYS` (por defecto `1`, lunes) la pausa no se aplica esos días de la semana, de modo que hay al menos una comprobación completa semanal. Un producto recién añadido que nunca se ha buscado en la API se busca igualmente la primera vez, aunque haya pausa. Para una comprobación completa puntual durante la pausa, lanza el flujo a mano con la casilla **force_api** marcada. Si defines además la variable `REMINDER_DATE` (AAAA-MM-DD), ese día el flujo te envía un recordatorio por Telegram con el texto de `REMINDER_TEXT` (opcional) y cómo reactivar la API.

`SEARCH_MODE` controla la fuente de tiendas del catálogo: `auto` (Google Shopping si hay clave; si no, tiendas), `shopping`, `stores` o `both`.

## Catálogo de tiendas (`stores.json`)

Incluye de partida Amazon.es, PcComponentes, MediaMarkt, El Corte Inglés, Fnac, Carrefour, Worten, Coolmod, Decathlon, Leroy Merlin e idealo. Cada tienda indica:

```json
{
  "id": "mediamarkt",
  "name": "MediaMarkt",
  "searchUrl": "https://www.mediamarkt.es/es/search.html?query={query}",
  "shipsToSpain": true,
  "pickupMadrid": true,
  "pickupNote": "Recogida gratuita en tiendas de Madrid.",
  "selectors": { "result": "...", "title": "...", "price": "...", "link": "..." }
}
```

Solo se consultan tiendas con `shipsToSpain` o `pickupMadrid` a `true`. Para añadir una tienda basta con añadir una entrada; si no indicas `selectors`, se usa una heurística genérica (JSON-LD `ItemList` o tarjetas con enlace y precio en euros).

> **Importante:** los selectores CSS del catálogo son un punto de partida. Las tiendas cambian su HTML y algunas (Amazon especialmente) bloquean el acceso automático o cargan los precios con JavaScript. Si una tienda devuelve `0 resultados` o `HTTP 403`, revisa sus selectores con las herramientas de desarrollador del navegador, añade cabeceras, o vigila ese producto por URL concreta. El comando `check` muestra por tienda cuántos resultados encontró y cuántos encajaron.

## Avisos por Telegram

Telegram es gratuito, oficial y no necesita ningún servicio intermedio. Alta, una sola vez:

1. En Telegram abre **@BotFather**, envía `/newbot`, elige un nombre (por ejemplo "OffersFinder") y un usuario que termine en `bot`. BotFather te devuelve un **token** como `123456789:AAH...`.
2. Pon ese token en `.env` como `TELEGRAM_BOT_TOKEN`.
3. Abre tu bot en Telegram (BotFather te da el enlace) y pulsa **Iniciar**. Esto es imprescindible: un bot solo puede escribir a quien le ha hablado antes.
4. Ejecuta `offersfinder telegram-setup`: consulta a Telegram quién ha escrito al bot y te muestra tu `TELEGRAM_CHAT_ID`. Ponlo en `.env`.
5. Ejecuta `offersfinder test-notify`: debe llegarte un mensaje de prueba.

| Variable | Valor |
|---|---|
| `TELEGRAM_BOT_TOKEN` | Token del bot que te dio BotFather. |
| `TELEGRAM_CHAT_ID` | Tu identificador de chat (lo muestra `telegram-setup`). |
| `MIN_DROP_PERCENT` | Bajada mínima (en %) para avisar; `0` avisa ante cualquier bajada. |

**Email (opcional)**: define `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASSWORD` y `EMAIL_TO`. Con Gmail, usa una [contraseña de aplicación](https://myaccount.google.com/apppasswords). Se envía un solo correo HTML por ejecución con todos los avisos.

**WhatsApp (opcional)**: a través de [CallMeBot](https://www.callmebot.com/blog/free-api-whatsapp-messages/), con `WHATSAPP_PHONE` y `WHATSAPP_APIKEY`. Su bot gratuito está a menudo lleno y no admite registros nuevos; la alternativa oficial (API de WhatsApp Business de Meta) exige cuenta de desarrollador y plantillas aprobadas y no está implementada.

Puedes activar varios canales a la vez.

## Instalar tu propia copia (para compartir)

Todo lo personal va en secretos y variables del repositorio; el código no contiene datos de nadie. Para que otra persona tenga su propio OffersFinder:

1. **Copia el repositorio** en tu cuenta de GitHub con el botón **Use this template → Create a new repository** (o con *Fork*). La rama por defecto debe contener el flujo `.github/workflows/check-prices.yml`.
2. **Lista de productos**: edita `watchlist.json` (viene con ejemplos) con lo que quieras vigilar.
3. **Secretos** (*Settings → Secrets and variables → Actions → Secrets*):
   - `SERPAPI_API_KEY` o `SERPER_API_KEY`: su clave de Google Shopping (ver "Fuente de precios").
   - `TELEGRAM_BOT_TOKEN`: el token de su propio bot (uno por persona, creado con @BotFather).
   - `TELEGRAM_CHAT_ID`: su chat (lo muestra `telegram-setup`, o la URL `getUpdates` del README).
4. **Variables opcionales** (*Settings → Secrets and variables → Actions → Variables*): `API_PAUSE_UNTIL`, `REMINDER_DATE`, `REMINDER_TEXT`, `SUMMARY_DAILY` (`false` para no recibir el resumen diario), `REPORT_DAY` (día del informe con gráficas, 1 = lunes; `none` para desactivarlo), `API_PAUSE_EXCEPT_WEEKDAYS` (días en que la pausa de la API no se aplica, por defecto `1`, lunes), `WEB_SEARCH`, `MIN_DROP_PERCENT`.
5. **Permisos**: *Settings → Actions → General → Workflow permissions → Read and write permissions*, para que el flujo guarde el histórico.
6. **Prueba**: *Actions → Comprobar precios → Run workflow* con la casilla *test_notify* para comprobar Telegram, y después una ejecución normal.

La programación (cada día a las 9:05 hora de España), el catálogo de tiendas y las listas negras se pueden cambiar editando el flujo, `stores.json` y `watchlist.json`. Licencia MIT.

## Ejecución automática

### Opción A: GitHub Actions (sin servidor propio)

El flujo `.github/workflows/check-prices.yml` ejecuta la comprobación **una vez al día, a partir de las 9 hora de España**. GitHub lanza las tareas programadas con retraso variable (a veces de horas), así que el flujo tiene varios disparos repartidos por la mañana y la tarde y ejecuta la comprobación en el primero que llega cada día, guardando la fecha en `data/last-run.txt` para omitir los demás. Guarda `data/history.json` y `data/report.html` en el repositorio, de modo que el histórico y el informe se acumulan entre ejecuciones.

1. Sube el repositorio a GitHub.
2. En *Settings → Secrets and variables → Actions → New repository secret* crea los secretos `SERPER_API_KEY` (o `SERPAPI_API_KEY`) para la fuente de precios, `TELEGRAM_BOT_TOKEN` (token del bot) y `TELEGRAM_CHAT_ID` (tu chat). Opcionalmente, los secretos de email (`SMTP_*`, `EMAIL_TO`) o WhatsApp, y las variables `MIN_DROP_PERCENT`, `API_PAUSE_UNTIL` o `REMINDER_DATE`.
3. En *Settings → Actions → General → Workflow permissions* marca **Read and write permissions** (necesario para guardar el histórico).
4. Cambia la frecuencia editando las expresiones `cron` del flujo si lo deseas; para varias comprobaciones al día quita el paso "Comprobar hora local" y usa un cron como `"5 */6 * * *"`. Ten en cuenta que cada producto con búsqueda consume una consulta de SerpApi/Serper por comprobación. Puedes lanzarlo a mano desde la pestaña *Actions* ("Run workflow").

Para ver el informe, abre `data/report.html` desde el repositorio (descárgalo o actívalo en GitHub Pages apuntando a la carpeta `data`).

### Opción B: en tu equipo

```bash
offersfinder check --loop --every 6h
```

O con el programador del sistema:

- **Linux/macOS (cron)**: `0 */6 * * * cd /ruta/OffersFinder && ./app/OffersFinder check >> offersfinder.log 2>&1`
- **Windows (Programador de tareas)**: crea una tarea que ejecute `C:\ruta\app\OffersFinder.exe check` cada 6 horas, con "Iniciar en" apuntando a la carpeta del proyecto.

## Ver la evolución del precio

```bash
offersfinder history "SSD 990 Pro"              # estadísticas de todo el histórico
offersfinder history "SSD 990 Pro" --month 2026-11
offersfinder report --open                      # gráficas interactivas
```

El informe muestra, por producto, la gráfica del precio en cada consulta con líneas de mínimo, máximo, media, mediana y objetivo, las tarjetas con esos valores y un selector de mes (global y por producto).

## Estructura del proyecto

```
src/OffersFinder/
  Program.cs                 CLI y comandos
  Models/                    WatchItem, Store, histórico, ajustes
  Services/PriceParser.cs    "1.234,56 €" -> 1234.56
  Services/PriceExtractor.cs precio de una página de producto
  Services/ShoppingSearchClient.cs  Google Shopping (España) vía Serper.dev
  Services/WebResultScanner.cs  lectura de las páginas de la búsqueda web de Google
  Services/OfferSearcher.cs  filtrado de ofertas y búsqueda directa en tiendas
  Services/PriceChecker.cs   reglas de aviso
  Services/Statistics.cs     mínimo, máximo, media, mediana, filtro por mes
  Services/ReportGenerator.cs informe HTML con gráficas (Chart.js)
  Notifications/Notifiers.cs consola, Telegram, email, WhatsApp
tests/OffersFinder.Tests/    pruebas xUnit
stores.json                  catálogo de tiendas
watchlist.json               productos a vigilar y lista negra global
watchlist.example.json       ejemplo de lista para empezar
data/history.json            histórico (generado)
data/report.html             informe (generado)
```

Pruebas: `dotnet test`.
