var editorGetHtml = function () {
    return tinymce.activeEditor.getContent();
}

var htmlContent;

var editorSetHtml = function (html) {
    if (tinymce.activeEditor) {
        tinymce.activeEditor.setContent(html);
    }
    else {
        // If not initialized yet, we need to delay it
        htmlContent = html;
    }
}

// Finestra di ricerca dei post e delle pagine del blog (api/links). Il link scelto va nel campo Url della
// finestra "Inserisci link"; il titolo diventa il testo del link se non ne è già stato scelto uno.
var openInternalLinkPicker = function (editor, callback) {
    var i18n = (window.BlogAdmin && BlogAdmin.i18n) || {};
    var windows = editor.windowManager.windows;
    var linkWindow = windows.length ? windows[windows.length - 1] : null;
    var results, timer, request;

    if (!document.getElementById('be-links-style')) {
        var css = '.mce-container .be-links-results{height:290px;overflow-y:auto;border:1px solid #ddd;background:#fff;}' +
            '.mce-container a.be-links-item{display:block;padding:6px 10px;border-bottom:1px solid #eee;cursor:pointer;white-space:normal;text-decoration:none;}' +
            '.mce-container a.be-links-item:hover{background:#eef5fb;}' +
            '.mce-container .be-links-title{display:block;color:#222;font-weight:bold;white-space:normal;}' +
            '.mce-container .be-links-info{display:block;color:#888;font-size:12px;white-space:normal;}' +
            '.mce-container .be-links-empty{padding:10px;color:#888;white-space:normal;}';
        $('<style id="be-links-style">').text(css).appendTo('head');
    }

    var encode = function (text) {
        return tinymce.DOM.encode(text || '');
    };

    var show = function (items) {
        if (!results) {
            return;
        }
        if (!items.length) {
            results.innerHTML = '<p class="be-links-empty">' + encode(i18n.linkPickerNoResults || 'No posts or pages found') + '</p>';
            return;
        }
        var html = '';
        tinymce.each(items, function (item, index) {
            var kind = item.Type === 'page' ? (i18n.page || 'Page') : (i18n.post || 'Post');
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

    var picker = editor.windowManager.open({
        title: i18n.linkPickerTitle || 'Link to a post or page',
        width: 560,
        height: 380,
        body: [
            {
                type: 'textbox', name: 'q', autofocus: true,
                onPostRender: function () {
                    this.getEl().setAttribute('placeholder', i18n.linkPickerSearch || 'Search by title...');
                },
                onkeyup: function () {
                    var text = this.value();
                    clearTimeout(timer);
                    timer = setTimeout(function () { search(text); }, 250);
                }
            },
            {
                type: 'container', minHeight: 290,
                html: '<div class="be-links-results"></div>',
                onPostRender: function () {
                    results = this.getEl().querySelector('.be-links-results');
                    search('');
                }
            }
        ],
        buttons: [{ text: i18n.cancel || 'Cancel', onclick: 'close' }]
    });

    var choose = function (item) {
        var meta = {};
        var textField = linkWindow ? linkWindow.find('#text')[0] : null;
        if (textField && !textField.value()) {
            meta.text = item.Title;
        }
        picker.close();
        callback(item.Url, meta);
    };
};

tinymce.init({
    selector: '#txtContent',
    plugins: [
        "advlist autolink lists link image charmap print preview anchor",
        "searchreplace visualblocks code fullscreen textcolor imagetools",
        "insertdatetime media table contextmenu paste sh4tinymce filemanager"
    ],
    toolbar: "styleselect | bold underline italic | alignleft aligncenter alignright | bullist numlist | forecolor backcolor | link media sh4tinymce | fullscreen code | filemanager",
    autosave_ask_before_unload: false,
    max_height: 1000,
    min_height: 300,
    height: 500,
    menubar: false,
    relative_urls: false,
    browser_spellcheck: true,
    paste_data_images: true,
    // Pulsante "Sfoglia" nelle finestre "Inserisci video" e "Inserisci immagine": il file scelto viene
    // caricato sul sito (i video nella cartella media del blog) e il suo indirizzo inserito nel campo Source.
    file_picker_callback: function (callback, value, meta) {
        if (meta.filetype === 'file') {
            // Pulsante "Sfoglia" della finestra "Inserisci link": scelta di un post o di una pagina del blog.
            openInternalLinkPicker(tinymce.activeEditor, callback);
            return;
        }
        if (meta.filetype !== 'media' && meta.filetype !== 'image') {
            return;
        }

        var input = document.createElement('input');
        input.type = 'file';
        input.accept = meta.filetype === 'media' ? 'video/*,audio/*' : 'image/*';
        input.onchange = function () {
            if (!input.files || input.files.length === 0) {
                return;
            }

            var data = new FormData();
            data.append('file', input.files[0]);
            var action = meta.filetype === 'media' ? 'video' : 'image';
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
                if (typeof toastr !== 'undefined') { toastr.success(BlogAdmin.i18n.uploaded || 'Uploaded'); }
            }).fail(function (xhr) {
                var message = xhr.responseJSON && xhr.responseJSON.Message ? xhr.responseJSON.Message : 'Upload failed';
                if (typeof toastr !== 'undefined') { toastr.error(message); } else { alert(message); }
            }).always(function () {
                if (typeof spinOff === 'function') { spinOff(); }
            });
        };
        input.click();
    },
    setup: function (editor) {
        editor.on('init', function (e) {
            if (htmlContent) {
                editor.setContent(htmlContent);
            }
        });
    }
});
