// Editor di post e pagine: TinyMCE 8 (licenza GPL), scaricato in wwwroot/lib/tinymce da tools/get-tinymce.ps1.
// Questo file contiene solo la configurazione e i pulsanti aggiunti per il blog.

var htmlContent;

var blogEditor = function () {
    var editor = window.tinymce ? tinymce.get('txtContent') : null;
    return editor && editor.initialized ? editor : null;
};

var editorGetHtml = function () {
    var editor = blogEditor();
    return editor ? editor.getContent() : (htmlContent || '');
};

var editorSetHtml = function (html) {
    var editor = blogEditor();
    if (editor) {
        editor.setContent(html);
    }
    else {
        // L'editor non è ancora pronto: il testo viene caricato appena lo è.
        htmlContent = html;
    }
};

var editorFocus = function () {
    var editor = blogEditor();
    if (editor) {
        editor.focus();
    }
};

var blogEditorLabel = function (key, fallback) {
    var i18n = (window.BlogAdmin && BlogAdmin.i18n) || {};
    return i18n[key] || fallback;
};

// Finestra di ricerca dei post e delle pagine del blog (api/links), aperta dal pulsante "Sfoglia" della finestra
// "Inserisci link". Il link scelto va nel campo Url; il titolo diventa il testo del link se non ne è già stato scelto uno.
var openInternalLinkPicker = function (editor, callback) {
    var results, timer, request, picker;
    var dialogId = 'be-links-' + new Date().getTime();

    if (!document.getElementById('be-links-style')) {
        // ".tox" davanti: la skin di TinyMCE azzera gli stili degli elementi delle sue finestre.
        var css = '.tox .be-links-results{height:290px;overflow-y:auto;border:1px solid #ccc;border-radius:3px;background:#fff;}' +
            '.tox a.be-links-item{display:block;padding:6px 10px;border-bottom:1px solid #eee;cursor:pointer;text-decoration:none;}' +
            '.tox a.be-links-item:hover,.tox a.be-links-item:focus{background:#eef5fb;}' +
            '.tox .be-links-title{display:block;color:#222;font-weight:bold;}' +
            '.tox .be-links-info{display:block;color:#777;font-size:12px;word-break:break-all;}' +
            '.tox .be-links-empty{padding:10px;color:#777;}';
        $('<style id="be-links-style">').text(css).appendTo('head');
    }

    var encode = function (text) {
        return tinymce.DOM.encode(text || '');
    };

    var choose = function (item) {
        picker.close();
        callback(item.Url, { text: item.Title });
    };

    var show = function (items) {
        if (!results) {
            return;
        }
        if (!items.length) {
            results.innerHTML = '<p class="be-links-empty">' + encode(blogEditorLabel('linkPickerNoResults', 'No posts or pages found')) + '</p>';
            return;
        }
        var html = '';
        tinymce.each(items, function (item, index) {
            var kind = item.Type === 'page' ? blogEditorLabel('page', 'Page') : blogEditorLabel('post', 'Post');
            html += '<a href="#" class="be-links-item" data-index="' + index + '">' +
                '<span class="be-links-title">' + encode(item.Title) + '</span>' +
                '<span class="be-links-info">' + encode(kind) + ' · ' + encode(item.Date) + ' · ' + encode(item.Url) + '</span></a>';
        });
        results.innerHTML = html;
        tinymce.each(results.querySelectorAll('.be-links-item'), function (link) {
            link.onclick = function (e) {
                e.preventDefault();
                choose(items[parseInt(link.getAttribute('data-index'), 10)]);
            };
        });
    };

    var search = function (text) {
        if (request) {
            request.abort();
        }
        request = $.ajax({
            url: SiteVars.RelativeWebRoot + 'api/links',
            data: { q: text, take: 30 },
            headers: { 'x-blog-instance': SiteVars.BlogInstanceId },
            dataType: 'json'
        }).done(show);
    };

    picker = editor.windowManager.open({
        title: blogEditorLabel('linkPickerTitle', 'Link to a post or page'),
        size: 'medium',
        body: {
            type: 'panel',
            items: [
                { type: 'input', name: 'q', placeholder: blogEditorLabel('linkPickerSearch', 'Search by title...') },
                { type: 'htmlpanel', html: '<div class="be-links-results" id="' + dialogId + '"></div>' }
            ]
        },
        initialData: { q: '' },
        buttons: [{ type: 'cancel', text: blogEditorLabel('cancel', 'Cancel') }],
        onChange: function (api, details) {
            if (details.name === 'q') {
                var text = api.getData().q;
                clearTimeout(timer);
                timer = setTimeout(function () { search(text); }, 250);
            }
        }
    });
    results = document.getElementById(dialogId);
    search('');
};

// Pulsante "Codice": blocco <pre class="brush: ..."> evidenziato sul sito da SyntaxHighlighter
// (stesso formato del vecchio plugin sh4tinymce, così i post già scritti restano modificabili).
var codeLanguages = [
    { text: 'HTML', value: 'html' }, { text: 'C#', value: 'csharp' }, { text: 'JavaScript', value: 'js' },
    { text: 'CSS', value: 'css' }, { text: 'SQL', value: 'sql' }, { text: 'XML', value: 'xml' },
    { text: 'PowerShell', value: 'ps' }, { text: 'Bash/shell', value: 'bash' }, { text: 'Python', value: 'py' },
    { text: 'PHP', value: 'php' }, { text: 'Java', value: 'java' }, { text: 'C++', value: 'cpp' },
    { text: 'Visual Basic', value: 'vb' }, { text: 'Ruby', value: 'ruby' }, { text: 'Perl', value: 'perl' },
    { text: 'Diff', value: 'diff' }, { text: 'Text', value: 'plain' }
];

var brushPattern = /brush\s*:\s*([^;\s]+)/i;

// Pulsante "Inserisci sottopagine" (solo nell'editor delle pagine): scrive il segnaposto [sottopagine ...] con le
// opzioni scelte. Al suo posto il sito mostra l'elenco aggiornato delle pagine figlie (vedi SubpagesPlaceholder.cs).
var blogEditorIsPage = function () {
    return /\/editpage(\.cshtml)?/i.test(window.location.pathname);
};

var openSubpagesDialog = function (editor) {
    var italian = ((window.SiteVars && SiteVars.Language) || '').toLowerCase().indexOf('it') === 0;
    var words = italian
        ? { tag: 'sottopagine', style: 'stile', columns: 'colonne', images: 'immagini', title: 'titolo', order: 'ordine', yes: 'si',
            styles: ['elenco', 'numerato', 'descrizioni', 'schede', 'pulsanti', 'riga'], orders: ['manuale', 'alfabetico', 'recenti'] }
        : { tag: 'subpages', style: 'style', columns: 'columns', images: 'images', title: 'title', order: 'order', yes: 'yes',
            styles: ['list', 'numbered', 'descriptions', 'cards', 'buttons', 'inline'], orders: ['manual', 'title', 'newest'] };
    var label = function (key, fallback) { return blogEditorLabel(key, fallback); };

    editor.windowManager.open({
        title: label('subpagesInsert', 'Insert subpages'),
        body: {
            type: 'panel',
            items: [
                { type: 'htmlpanel', html: '<p style="margin:0 0 8px">' + editor.dom.encode(label('subpagesInsertHelp', 'The list of child pages appears here and stays up to date by itself.')) + '</p>' },
                {
                    type: 'listbox', name: 'style', label: label('subpagesStyle', 'Style'), items: [
                        { value: 'page', text: label('subpagesStylePage', 'As set in the page') },
                        { value: '0', text: label('subpagesStyleList', 'Bulleted list') },
                        { value: '1', text: label('subpagesStyleNumbered', 'Numbered list') },
                        { value: '2', text: label('subpagesStyleDescriptions', 'List with descriptions') },
                        { value: '3', text: label('subpagesStyleCards', 'Cards') },
                        { value: '4', text: label('subpagesStyleButtons', 'Buttons') },
                        { value: '5', text: label('subpagesStyleInline', 'On one line') }
                    ]
                },
                { type: 'listbox', name: 'columns', label: label('subpagesColumns', 'Columns (cards)'), items: [{ value: '', text: '-' }, { value: '2', text: '2' }, { value: '3', text: '3' }, { value: '4', text: '4' }] },
                { type: 'checkbox', name: 'images', label: label('subpagesImages', 'Show the first image of each page (cards)') },
                {
                    type: 'listbox', name: 'order', label: label('subpagesOrder', 'Order'), items: [
                        { value: 'page', text: label('subpagesStylePage', 'As set in the page') },
                        { value: '0', text: label('subpagesOrderManual', 'Sort order') },
                        { value: '1', text: label('subpagesOrderTitle', 'Alphabetical') },
                        { value: '2', text: label('subpagesOrderNewest', 'Newest first') }
                    ]
                },
                { type: 'input', name: 'title', label: label('subpagesTitle', 'Title above the list'), placeholder: label('subpagesTitlePlaceholder', 'e.g. In this section') }
            ]
        },
        initialData: { style: 'page', columns: '', images: false, order: 'page', title: '' },
        buttons: [
            { type: 'cancel', text: label('cancel', 'Cancel') },
            { type: 'submit', text: label('subpagesInsertButton', 'Insert'), primary: true }
        ],
        onSubmit: function (api) {
            var data = api.getData();
            var parts = [words.tag];
            if (data.style !== 'page') parts.push(words.style + '=' + words.styles[+data.style]);
            if (data.columns) parts.push(words.columns + '=' + data.columns);
            if (data.images) parts.push(words.images + '=' + words.yes);
            if (data.order !== 'page') parts.push(words.order + '=' + words.orders[+data.order]);
            var title = (data.title || '').replace(/["\]]/g, '').trim();
            if (title) parts.push(words.title + '="' + title + '"');
            editor.insertContent('<p>' + editor.dom.encode('[' + parts.join(' ') + ']') + '</p>');
            api.close();
        }
    });
};

var openCodeDialog = function (editor) {
    var pre = editor.dom.getParent(editor.selection.getNode(), 'pre');
    var existing = pre && brushPattern.test(pre.className) ? pre : null;
    var language = existing ? brushPattern.exec(existing.className)[1].toLowerCase() : 'html';
    var code = existing ? existing.textContent : editor.selection.getContent({ format: 'text' });

    var items = codeLanguages.slice();
    if (!items.some(function (item) { return item.value === language; })) {
        // Linguaggio non in elenco (post scritti con il vecchio plugin): resta quello.
        items.push({ text: language, value: language });
    }

    editor.windowManager.open({
        title: blogEditorLabel('editorCodeTitle', 'Insert code'),
        size: 'large',
        body: {
            type: 'panel',
            items: [
                { type: 'listbox', name: 'language', label: blogEditorLabel('editorCodeLanguage', 'Language'), items: items },
                { type: 'textarea', name: 'code', label: blogEditorLabel('editorCode', 'Code'), maximized: true }
            ]
        },
        initialData: { language: language, code: code },
        buttons: [
            { type: 'cancel', text: blogEditorLabel('cancel', 'Cancel') },
            { type: 'submit', text: blogEditorLabel('save', 'Save'), primary: true }
        ],
        onSubmit: function (api) {
            var data = api.getData();
            var html = editor.dom.encode(data.code);
            editor.undoManager.transact(function () {
                if (existing) {
                    // Le altre impostazioni di SyntaxHighlighter già presenti (gutter, highlight...) restano.
                    editor.dom.setAttrib(existing, 'class', existing.className.replace(brushPattern, 'brush: ' + data.language));
                    editor.dom.setAttrib(existing, 'contenteditable', null);
                    existing.innerHTML = html;
                }
                else {
                    editor.insertContent('<pre class="brush: ' + editor.dom.encode(data.language) + '">' + html + '</pre><p></p>');
                }
            });
            api.close();
        }
    });
};

// Pulsante "File manager": la finestra (admin/app/editor/filemanager.cshtml) manda il codice da inserire con postMessage.
var openFileManager = function (editor) {
    editor.windowManager.openUrl({
        title: blogEditorLabel('fileManager', 'File manager'),
        url: SiteVars.RelativeWebRoot + 'admin/app/editor/filemanager.cshtml',
        width: Math.round(window.innerWidth * 0.9),
        height: Math.round(window.innerHeight * 0.8),
        onMessage: function (api, message) {
            if (message.mceAction === 'blogInsertFile' && typeof message.content === 'string') {
                editor.insertContent(message.content);
                api.close();
            }
        }
    });
};

// Pulsante "Sfoglia" delle finestre "Inserisci video" e "Inserisci immagine": il file scelto
// viene caricato sul sito (i video nella cartella media del blog) e il suo indirizzo inserito nel campo Source.
var uploadFromPicker = function (callback, filetype) {
    var input = document.createElement('input');
    input.type = 'file';
    input.accept = filetype === 'media' ? 'video/*,audio/*' : 'image/*';
    input.onchange = function () {
        if (!input.files || input.files.length === 0) {
            return;
        }

        var data = new FormData();
        data.append('file', input.files[0]);
        var action = filetype === 'media' ? 'video' : 'image';
        if (typeof spinOn === 'function') { spinOn(); }
        $.ajax({
            url: SiteVars.RelativeWebRoot + 'api/upload?action=' + action,
            type: 'POST',
            data: data,
            processData: false,
            contentType: false,
            headers: { 'x-blog-instance': SiteVars.BlogInstanceId }
        }).done(function (result) {
            var url = String(result).replace(/^"|"$/g, '');
            callback(action === 'video' ? SiteVars.RelativeWebRoot + 'media/' + encodeURIComponent(url) : url);
            if (typeof toastr !== 'undefined') { toastr.success(blogEditorLabel('uploaded', 'Uploaded')); }
        }).fail(function (xhr) {
            var message = xhr.responseJSON && xhr.responseJSON.Message ? xhr.responseJSON.Message : 'Upload failed';
            if (typeof toastr !== 'undefined') { toastr.error(message); } else { alert(message); }
        }).always(function () {
            if (typeof spinOff === 'function') { spinOff(); }
        });
    };
    input.click();
};

// Impostazioni comuni a tutti gli editor dell'amministrazione (anche quelli dei widget).
// Allineamento a sinistra/destra: TinyMCE 8 sposta le tabelle solo con i margini e toglie il float,
// quindi il testo non scorre accanto. Qui (come in TinyMCE 4 e nella vecchia versione del blog) la tabella
// diventa flottante, con un po' di spazio dal testo. Gli altri elementi restano come in TinyMCE 8.
var blogAlignFormats = function () {
    var side = function (align) {
        var tableStyles = { float: align, marginBottom: '10px' };
        tableStyles['margin' + (align === 'left' ? 'Right' : 'Left')] = '15px';
        tableStyles['margin' + (align === 'left' ? 'Left' : 'Right')] = '0px';
        return [
            { selector: 'figure.image', collapsed: false, classes: 'align-' + align, ceFalseOverride: true },
            { selector: 'figure,p,h1,h2,h3,h4,h5,h6,td,th,tr,div,ul,ol,li,pre', styles: { textAlign: align }, inherit: false },
            { selector: 'img,audio,video', collapsed: false, styles: { float: align } },
            { selector: '.mce-placeholder', styles: { float: align }, ceFalseOverride: true },
            { selector: 'table', collapsed: false, styles: tableStyles },
            { selector: '.mce-preview-object,[data-ephox-embed-iri],.tiny-pageembed', ceFalseOverride: true, styles: { float: align } }
        ];
    };
    return { alignleft: side('left'), alignright: side('right') };
};

var blogEditorSettings = function (settings) {
    var language = (window.SiteVars && SiteVars.Language) || '';
    var common = {
        license_key: 'gpl',
        promotion: false,
        // L'interfaccia in italiano quando l'amministrazione è in italiano (traduzione in lib/tinymce/langs).
        language: language.toLowerCase().indexOf('it') === 0 ? 'it' : undefined,
        relative_urls: false,
        browser_spellcheck: true,
        contextmenu: false,
        // Il contenuto dei post esistenti resta com'è: niente sandbox sugli iframe (video incorporati)
        // e niente conversione di object/embed.
        sandbox_iframes: false,
        convert_unsafe_embeds: false,
        formats: blogAlignFormats()
    };
    return $.extend(common, settings);
};

if (document.getElementById('txtContent')) {
    tinymce.init(blogEditorSettings({
        selector: '#txtContent',
        plugins: 'advlist autolink lists link image charmap preview anchor searchreplace visualblocks code fullscreen insertdatetime media table',
        toolbar: 'blocks | bold underline italic | alignleft aligncenter alignright | bullist numlist | forecolor backcolor | ' +
            'link unlink image media table blogcode blogmore' + (blogEditorIsPage() ? ' blogsubpages' : '') + ' | fullscreen code | blogfiles',
        menubar: false,
        // Come in TinyMCE 4: se la finestra è stretta i pulsanti vanno a capo invece di finire nel menu "...".
        toolbar_mode: 'wrap',
        height: 500,
        min_height: 300,
        max_height: 1000,
        paste_data_images: true,
        image_advtab: true,
        // Barra che compare dentro una tabella: oltre alle proprietà della tabella anche quelle di riga e cella
        // (in TinyMCE 4 erano nel menu del tasto destro). Per una colonna: selezionarne le celle e usare "Proprietà cella".
        table_toolbar: 'tableprops tabledelete | tablerowprops tableinsertrowbefore tableinsertrowafter tabledeleterow | ' +
            'tablecellprops tablemergecells tablesplitcells | tableinsertcolbefore tableinsertcolafter tabledeletecol',
        file_picker_callback: function (callback, value, meta) {
            if (meta.filetype === 'file') {
                // Pulsante "Sfoglia" della finestra "Inserisci link": scelta di un post o di una pagina del blog.
                openInternalLinkPicker(tinymce.activeEditor, callback);
                return;
            }
            if (meta.filetype === 'media' || meta.filetype === 'image') {
                uploadFromPicker(callback, meta.filetype);
            }
        },
        setup: function (editor) {
            editor.ui.registry.addButton('blogcode', {
                icon: 'code-sample',
                tooltip: blogEditorLabel('editorCodeTitle', 'Insert code'),
                onAction: function () { openCodeDialog(editor); }
            });
            editor.ui.registry.addButton('blogmore', {
                text: '[more]',
                tooltip: blogEditorLabel('editorMoreTooltip', 'Break the post here in lists ([more])'),
                onAction: function () { editor.insertContent('<p>[more]</p>'); }
            });
            editor.ui.registry.addButton('blogsubpages', {
                icon: 'unordered-list',
                text: blogEditorLabel('subpagesButton', 'Subpages'),
                tooltip: blogEditorLabel('subpagesInsert', 'Insert subpages'),
                onAction: function () { openSubpagesDialog(editor); }
            });
            editor.ui.registry.addButton('blogfiles', {
                icon: 'browse',
                tooltip: blogEditorLabel('fileManager', 'File manager'),
                onAction: function () { openFileManager(editor); }
            });
            editor.on('init', function () {
                if (htmlContent) {
                    editor.setContent(htmlContent);
                }
            });
        }
    }));
}
