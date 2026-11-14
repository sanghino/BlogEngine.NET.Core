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
        convert_unsafe_embeds: false
    };
    return $.extend(common, settings);
};

if (document.getElementById('txtContent')) {
    tinymce.init(blogEditorSettings({
        selector: '#txtContent',
        plugins: 'advlist autolink lists link image charmap preview anchor searchreplace visualblocks code fullscreen insertdatetime media table',
        toolbar: 'blocks | bold underline italic | alignleft aligncenter alignright | bullist numlist | forecolor backcolor | ' +
            'link unlink image media table blogcode blogmore | fullscreen code | blogfiles',
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
