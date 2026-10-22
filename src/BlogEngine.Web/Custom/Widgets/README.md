# Widget di BlogEngine

Un widget è una cartella con il template `widget.liquid` e il manifest `widget.json`; si installa
copiandola in `Custom/Widgets/<Nome>` (o in `App_Data/widgets/<Nome>`) e compare subito tra i widget
disponibili in *Personalizza → Widget*.

Nel template sono disponibili `widget` (`id`, `name`, `title`, `zone`, `settings`, `data`) e tutte le
variabili dei temi (`blog`, `user`, `request`, filtri `t`, `app_url`, ...). Esempio:

```liquid
<div class="Widget widget saluto">
    <h4 class="WidgetHeader widget-header">{{ widget.title }}</h4>
    <div class="WidgetContent widget-content">{{ widget.settings.messaggio | default: 'Ciao!' }}</div>
</div>
```

## Impostazioni

Le impostazioni descritte nel manifest generano da sole la pagina di modifica del widget:

```json
{
  "title": "Saluto",
  "description": "Un messaggio di benvenuto",
  "version": "1.0",
  "settings": [
    { "key": "messaggio", "type": "text", "label": "Messaggio", "default": "Ciao!" },
    { "key": "colore", "type": "select", "label": "Colore", "default": "blu",
      "options": [ { "value": "blu", "text": "Blu" }, { "value": "rosso", "text": "Rosso" } ] }
  ]
}
```

Tipi disponibili: `text`, `number`, `checkbox`, `select`, `html` (con editor), `code` (testo incollato così com'è,
senza editor: script, codici pubblicitari, embed), `category`, `author`,
`links` (elenco di link), `newsletter` (iscritti), `blogroll` (rimanda alla pagina del blogroll).
`label` può essere una chiave delle traduzioni (es. `numberOfPosts`) o un testo libero.

## Dati preparati dal codice

I widget inclusi ricevono in `widget.data` dati calcolati dal sito (post recenti, categorie, mesi...),
forniti da classi che implementano `IWidgetDataProvider`. Anche le estensioni potranno registrarne
di nuove per i propri widget.
