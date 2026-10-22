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
