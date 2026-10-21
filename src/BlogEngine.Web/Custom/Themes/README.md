# Temi di BlogEngine (Liquid)

Un tema è una cartella con alcuni file di testo in linguaggio [Liquid](https://shopify.github.io/liquid/)
più i suoi CSS, script e immagini. Il sito legge i template a ogni modifica: per installare o
aggiornare un tema basta copiarne la cartella, senza ricompilare né riavviare.

## Dove si mettono

- `Custom/Themes/<Nome>` – temi inclusi nel pacchetto o copiati via FTP;
- `App_Data/themes/<Nome>` – temi caricati dall'amministrazione (cartella scrivibile).

Il nome della cartella è l'identificativo del tema. I file statici sono pubblicati all'indirizzo
`/Custom/Themes/<Nome>/...` (anche quelli in `App_Data/themes`); i file `.liquid` non sono mai scaricabili.
Per provare un tema senza attivarlo: `https://sito/?theme=<Nome>`.

## File del tema

| File | Uso |
|------|-----|
| `theme.json` | titolo, descrizione, autori, versione e widget predefiniti delle zone |
| `layout.liquid` | **obbligatorio**: la struttura della pagina (come il vecchio `site.master`) |
| `layout-post.liquid`, `layout-page.liquid` | facoltativi: layout del post e delle altre pagine (come `post.master` e `page.master`) |
| `post-view.liquid` | un post nell'elenco (`location == 'list'`) o da solo (`'single'`) – era `PostView.ascx` |
| `comment-view.liquid`, `comment-form.liquid` | commento e modulo dei commenti |
| `post-navigation.liquid`, `related-posts.liquid` | post precedente/successivo e post correlati |
| `widgets/<Widget>.liquid` | facoltativo: aspetto diverso per un widget |

Ogni template non presente nel tema viene preso da `Templates/` (le versioni predefinite):
`index`, `post`, `page`, `archive`, `search`, `contact`, `calendar`, `error`, `comments`, `pager`, ...
Un tema può ridefinire anche questi.

Esempio di `theme.json`:

```json
{
  "title": "Il mio tema",
  "description": "Tema chiaro a una colonna",
  "authors": "Mario Rossi",
  "version": "1.0",
  "defaultWidgets": { "sidebar": [ "Search", "CategoryList", "TagCloud" ] }
}
```

## Il layout

```liquid
<!DOCTYPE html>
<html lang="{{ blog.language }}">
<head>
    <meta charset="utf-8" />
    <title>{{ page_title }}</title>
    <link href="{{ 'css/style.css' | theme_url }}" rel="stylesheet" />
    {{ content_for_header }}
</head>
<body>
    <h1><a href="{{ blog.url }}">{{ blog.name }}</a></h1>
    {{ content_for_layout }}
    {% widget_zone 'sidebar' %}
    {{ content_for_footer }}
</body>
</html>
```

- `content_for_header` (obbligatorio): meta tag, feed, jQuery, `Scripts/blog.js` e le variabili per gli script;
- `content_for_layout`: il contenuto della pagina;
- `content_for_footer`: lo script di monitoraggio delle impostazioni;
- `{% widget_zone 'nome' %}`: una zona di widget, gestita da *Personalizza → Widget*.

## Variabili principali

- `blog`: `name`, `description`, `url`, `absolute_url`, `language`, `year`, `pages` (menu con `children`),
  `archive_url`, `contact_url`, `search_url`, `login_url`, `admin_url`, `feed_url`, `version`;
- `user`: `is_authenticated`, `name`, `is_admin`, `can_access_admin`;
- `template`: nome della pagina (`index`, `post`, `page`, `archive`, ...);
- `posts` e `pager` (elenco), `post` e `comments` (post singolo), `page` (pagina);
- nei nomi delle proprietà si usa il formato `snake_case`: `post.date_created`, `post.author_name`.

I testi sono già codificati per l'HTML; i campi che contengono HTML (`post.body`, `page.content`,
`comment.text`, `post.admin_links`, ...) vengono scritti così come sono.

## Filtri aggiunti

| Filtro | Esempio |
|--------|---------|
| `t` – testo tradotto | `{{ 'addComment' \| t }}`, `{{ 'currentlyRated' \| t: post.rating, post.raters }}` |
| `theme_url` – file del tema | `{{ 'img/logo.png' \| theme_url }}` |
| `app_url` – file comuni del sito | `{{ 'Content/images/blog/rssButton.png' \| app_url }}` |
| `blog_url` – indirizzo nel blog | `{{ 'archive' \| blog_url }}` |
| `theme_field` – campo personalizzato | `{{ 'Facebook Account' \| theme_field: '#' }}` |
| `date_format` – data in formato .NET | `{{ post.date_created \| date_format: 'dd MMMM yyyy' }}` |
| `json` – stringa per script e JSON-LD | `"headline": {{ post.title \| json }}` |

I campi usati con `theme_field` compaiono da soli in *Personalizza → Temi → campi personalizzati*,
con il valore predefinito indicato nel template (sostituiscono i vecchi `[CUSTOMFIELD|THEME|...]`).

## Commenti

`Scripts/blog.js` invia i commenti senza ricaricare la pagina e cerca gli elementi per `id`:
`commentlist`, `comment-form`, `status`, `txtName`, `txtEmail`, `txtWebsite`, `txtContent`,
`cbNotify`, `btnSaveAjax`, `cancelReply`, `id_<commento>` e `replies_<commento>`. Un tema che
ridefinisce `comment-form.liquid` o `comment-view.liquid` deve mantenere questi id.
