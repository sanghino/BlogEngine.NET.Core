// Script delle pagine pubbliche (porting di Scripts/Auto/blog.js).
// Le variabili del blog arrivano da BlogEngineRes, scritto nell'intestazione della pagina:
// webRoot, applicationWebRoot, blogInstanceId, antiforgery, antiforgeryHeader e le etichette i18n.
// I nomi delle funzioni (BlogEngine.replyToComment, BlogEngine.search, ...) sono quelli dei vecchi temi.
var BlogEngine = {
    $: function (id) {
        return document.getElementById(id);
    },

    i18n: function (key) {
        return (window.BlogEngineRes && BlogEngineRes.i18n && BlogEngineRes.i18n[key]) || key;
    },

    // Invio in POST con il token antifalsificazione; restituisce una Promise con la risposta JSON.
    post: function (path, data) {
        var body = new FormData();
        for (var key in data) {
            if (Object.prototype.hasOwnProperty.call(data, key) && data[key] !== undefined && data[key] !== null) {
                body.append(key, data[key]);
            }
        }

        var headers = {};
        headers[BlogEngineRes.antiforgeryHeader || 'RequestVerificationToken'] = BlogEngineRes.antiforgery;
        return fetch(BlogEngineRes.webRoot + path, { method: 'POST', body: body, headers: headers, credentials: 'same-origin' })
            .then(function (response) {
                return response.text().then(function (text) {
                    var json = {};
                    try { json = text ? JSON.parse(text) : {}; } catch (e) { json = { message: text }; }
                    if (!response.ok) {
                        var error = new Error(json.message || response.statusText || 'Error');
                        error.status = response.status;
                        throw error;
                    }
                    return json;
                });
            });
    },

    setFlag: function (iso) {
        var flag = BlogEngine.$('imgFlag');
        if (!flag) {
            return;
        }
        flag.src = BlogEngineRes.applicationWebRoot + (iso && iso.length > 0
            ? 'Content/images/blog/flags/' + iso + '.png'
            : 'Content/images/blog/pixel.png');
    },

    // ------------------------------------------------------------------ commenti

    comments: {
        postId: null,
        postUrl: null,
        moderation: false,
        nameBox: null,
        emailBox: null,
        websiteBox: null,
        countryDropDown: null,
        contentBox: null,
        replyToId: null,

        // Collega i campi del modulo e ripropone i dati dell'ultimo commento (prima in un cookie, ora nel browser).
        init: function () {
            var c = BlogEngine.comments;
            c.nameBox = BlogEngine.$('txtName');
            c.emailBox = BlogEngine.$('txtEmail');
            c.websiteBox = BlogEngine.$('txtWebsite');
            c.countryDropDown = BlogEngine.$('ddlCountry');
            c.contentBox = BlogEngine.$('txtContent');
            c.replyToId = BlogEngine.$('hiddenReplyTo');
            try {
                var saved = JSON.parse(window.localStorage.getItem('blogengine.commenter') || '{}');
                if (c.nameBox && !c.nameBox.value && saved.name) c.nameBox.value = saved.name;
                if (c.emailBox && !c.emailBox.value && saved.email) c.emailBox.value = saved.email;
                if (c.websiteBox && !c.websiteBox.value && saved.website) c.websiteBox.value = saved.website;
            } catch (e) { /* archivio del browser non disponibile */ }

            // Anteprima mentre si scrive (se il tema prevede l'elemento commentPreview).
            var preview = BlogEngine.$('commentPreview');
            if (preview && c.contentBox) {
                c.contentBox.addEventListener('input', function () {
                    preview.textContent = c.contentBox.value;
                    preview.innerHTML = preview.innerHTML.replace(/\n/g, '<br />');
                });
            }
        }
    },

    status: function (text, className) {
        var status = BlogEngine.$('status');
        if (status) {
            status.className = className || '';
            status.textContent = text || '';
        }
    },

    toggleCommentSavingIndicators: function (saving) {
        var button = BlogEngine.$('btnSaveAjax');
        var loader = BlogEngine.$('ajaxLoader');
        if (button) button.disabled = saving;
        if (loader) loader.style.display = saving ? 'inline' : 'none';
    },

    validateAndSubmitCommentForm: function () {
        var c = BlogEngine.comments;
        if (!c.nameBox) {
            c.init();
        }

        var checks = [
            [c.nameBox, BlogEngine.i18n('name')],
            [c.emailBox, BlogEngine.i18n('email')],
            [c.contentBox, BlogEngine.i18n('comment')]
        ];
        for (var i = 0; i < checks.length; i++) {
            if (!checks[i][0] || checks[i][0].value.trim().length < 1) {
                BlogEngine.status(checks[i][1] + ': ' + BlogEngine.i18n('required'), 'warning');
                if (checks[i][0]) checks[i][0].focus();
                return false;
            }
        }

        if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(c.emailBox.value.trim())) {
            BlogEngine.status(BlogEngine.i18n('enterValidEmail'), 'warning');
            c.emailBox.focus();
            return false;
        }

        BlogEngine.addComment();
        return false;
    },

    addComment: function () {
        var c = BlogEngine.comments;
        var notify = BlogEngine.$('cbNotify');
        var honeypot = BlogEngine.$('txtHp');
        var data = {
            postId: c.postId,
            parentId: c.replyToId && c.replyToId.value ? c.replyToId.value : '',
            author: c.nameBox.value,
            email: c.emailBox.value,
            website: c.websiteBox ? c.websiteBox.value : '',
            country: c.countryDropDown ? c.countryDropDown.value : '',
            content: c.contentBox.value,
            notify: notify ? notify.checked : false,
            hp: honeypot ? honeypot.value : ''
        };

        // Campi dei captcha delle estensioni (es. la risposta di SimpleCaptcha o il token di reCAPTCHA).
        var captchaFields = document.querySelectorAll('#commentCaptcha input[name], #commentCaptcha textarea[name], #commentCaptcha select[name]');
        for (var f = 0; f < captchaFields.length; f++) {
            data[captchaFields[f].name] = captchaFields[f].value;
        }

        BlogEngine.toggleCommentSavingIndicators(true);
        BlogEngine.status(BlogEngine.i18n('savingTheComment'), '');

        BlogEngine.post('comment', data).then(function (result) {
            try {
                window.localStorage.setItem('blogengine.commenter', JSON.stringify({ name: data.author, email: data.email, website: data.website }));
            } catch (e) { /* ignorato */ }

            BlogEngine.appendComment(result.html, data.parentId);
            c.contentBox.value = '';
            BlogEngine.toggleCommentSavingIndicators(false);
            BlogEngine.status(result.approved && !c.moderation ? BlogEngine.i18n('commentWasSaved') : BlogEngine.i18n('commentWaitingModeration'), 'success');
            BlogEngine.cancelReply();

            if (typeof OnComment !== 'undefined') {
                OnComment(data.author, data.email, data.website, data.country, data.content);
            }
        }).catch(function (error) {
            BlogEngine.toggleCommentSavingIndicators(false);
            if (window.grecaptcha && window.grecaptcha.reset) {
                window.grecaptcha.reset();
            }
            BlogEngine.status(error.status === 429 ? 'Too many requests, please try again later.' : error.message, 'warning');
        });
    },

    appendComment: function (html, parentId) {
        if (!html) {
            return;
        }

        var target = parentId ? BlogEngine.$('replies_' + parentId) : BlogEngine.$('commentlist');
        if (!target) {
            return;
        }

        var holder = document.createElement('div');
        holder.innerHTML = html;
        while (holder.firstChild) {
            target.appendChild(holder.firstChild);
        }

        target.style.display = '';
    },

    cancelReply: function () {
        BlogEngine.replyToComment('');
    },

    // Sposta il modulo sotto il commento a cui si risponde (o in fondo all'elenco).
    replyToComment: function (id) {
        var c = BlogEngine.comments;
        if (!c.nameBox) {
            c.init();
        }
        if (c.replyToId) {
            c.replyToId.value = id || '';
        }

        var form = BlogEngine.$('comment-form');
        var cancel = BlogEngine.$('cancelReply');
        if (!form) {
            return;
        }

        if (!id || id === '00000000-0000-0000-0000-000000000000') {
            var list = BlogEngine.$('commentlist');
            if (list && list.parentNode) {
                list.parentNode.parentNode.insertBefore(form, list.parentNode.nextSibling);
            }
            if (cancel) cancel.style.display = 'none';
        } else {
            var replies = BlogEngine.$('replies_' + id);
            if (!replies) {
                replies = document.createElement('div');
                replies.className = 'comment-replies';
                replies.id = 'replies_' + id;
                BlogEngine.$('id_' + id).appendChild(replies);
            }
            replies.style.display = '';
            replies.appendChild(form);
            if (cancel) cancel.style.display = '';
            if (c.nameBox) c.nameBox.focus();
        }
    },

    // ------------------------------------------------------------------ comandi rapidi di moderazione

    moderate: function (command, id, confirmText) {
        if (confirmText && !window.confirm(confirmText)) {
            return false;
        }

        BlogEngine.post('moderation/' + command + '/' + id, {}).then(function (result) {
            if (result && result.redirect) {
                window.location.href = result.redirect;
            } else {
                window.location.reload();
            }
        }).catch(function (error) {
            window.alert(error.message);
        });
        return false;
    },

    approveComment: function (id) {
        return BlogEngine.moderate('approve-comment', id);
    },

    deleteComment: function (id, withReplies) {
        return BlogEngine.moderate(withReplies ? 'delete-comment-and-replies' : 'delete-comment', id, BlogEngine.i18n('areYouSureDeleteComment'));
    },

    approveAllComments: function (postId) {
        return BlogEngine.moderate('approve-all', postId);
    },

    deletePost: function (postId) {
        return BlogEngine.moderate('delete-post', postId, BlogEngine.i18n('areYouSure').replace('{0}', BlogEngine.i18n('delete').toLowerCase()).replace('{1}', ''));
    },

    deletePage: function (pageId) {
        return BlogEngine.moderate('delete-page', pageId, BlogEngine.i18n('areYouSure').replace('{0}', BlogEngine.i18n('delete').toLowerCase()).replace('{1}', ''));
    },

    // ------------------------------------------------------------------ ricerca

    search: function (root, searchfield) {
        var input = BlogEngine.$(searchfield || 'searchfield');
        var check = BlogEngine.$('searchcomments');
        var url = (root || BlogEngineRes.webRoot) + 'search?q=' + encodeURIComponent(input ? input.value : '');
        if (check && check.checked) {
            url += '&comment=true';
        }
        top.location.href = url;
        return false;
    },

    searchClear: function (defaultText, searchfield) {
        var input = BlogEngine.$(searchfield || 'searchfield');
        if (!input) return;
        if (input.value === defaultText) input.value = '';
        else if (input.value === '') input.value = defaultText;
    },

    // ------------------------------------------------------------------ newsletter (widget)

    subscribeNewsletter: function (widgetId) {
        var input = BlogEngine.$('txtNewsletterEmail' + (widgetId || ''));
        var message = BlogEngine.$('newsletterMessage' + (widgetId || ''));
        if (!input) return false;
        BlogEngine.post('newsletter', { email: input.value }).then(function (result) {
            var form = BlogEngine.$('newsletterform' + (widgetId || ''));
            if (form) form.innerHTML = '<h2 id="newsletteraction">' + (result.message || BlogEngine.i18n('thankYou')) + '</h2>';
        }).catch(function (error) {
            if (message) {
                message.textContent = error.message;
                message.style.display = '';
            }
        });
        return false;
    },

    // ------------------------------------------------------------------ voto con le stelle

    rate: function (blogId, id, rating) {
        BlogEngine.post('rating', { id: id, rating: rating }).then(function (result) {
            if (result.status === 'OK') {
                if (typeof OnRating !== 'undefined') OnRating(rating);
                window.alert(BlogEngine.i18n('ratingHasBeenRegistered'));
            } else if (result.status === 'HASRATED') {
                window.alert(BlogEngine.i18n('ratingHasBeenRegistered'));
            }
        }).catch(function () {
            window.alert('An error occured while registering your rating. Please try again');
        });
    },

    showRating: function (container, id, raters, rating, blogId) {
        var div = document.createElement('div');
        div.className = 'rating';

        var p = document.createElement('p');
        div.appendChild(p);
        p.textContent = Number(raters) === 0
            ? BlogEngine.i18n('beTheFirstToRate')
            : BlogEngine.i18n('currentlyRated').replace('{0}', Number(rating).toFixed(1)).replace('{1}', raters);

        var ul = document.createElement('ul');
        ul.className = 'star-rating small-star';
        div.appendChild(ul);

        var current = document.createElement('li');
        current.className = 'current-rating';
        current.style.width = Math.round(rating * 20) + '%';
        current.textContent = 'Currently ' + rating + '/5 Stars.';
        ul.appendChild(current);

        var names = ['one-star', 'two-stars', 'three-stars', 'four-stars', 'five-stars'];
        for (var i = 1; i <= 5; i++) {
            var li = document.createElement('li');
            var a = document.createElement('a');
            a.textContent = i;
            a.href = '#';
            a.className = names[i - 1];
            a.title = BlogEngine.i18n('rateThisXStars').replace('{0}', i.toString()).replace('{1}', i === 1 ? '' : 's');
            a.rel = 'nofollow';
            a.onclick = function () {
                BlogEngine.rate(blogId, id, this.textContent);
                return false;
            };
            li.appendChild(a);
            ul.appendChild(li);
        }

        container.innerHTML = '';
        container.appendChild(div);
        container.style.visibility = 'visible';
    },

    applyRatings: function () {
        var divs = document.querySelectorAll('div.ratingcontainer');
        for (var i = 0; i < divs.length; i++) {
            var args = divs[i].textContent.split('|');
            BlogEngine.showRating(divs[i], args[0], args[1], args[2], args[3]);
        }
    },

    // ------------------------------------------------------------------ varie

    toggleMonth: function (year) {
        var list = BlogEngine.$(year);
        if (list) {
            list.className = list.className === 'open' ? '' : 'open';
        }
    },

    xfnRelationships: ['friend', 'acquaintance', 'contact', 'met', 'co-worker', 'colleague', 'co-resident', 'neighbor',
        'child', 'parent', 'sibling', 'spouse', 'kin', 'muse', 'crush', 'date', 'sweetheart', 'me'],

    // Mostra nel titolo dei link la relazione XFN (es. rel="friend met").
    hightLightXfn: function () {
        var links = document.querySelectorAll('a[rel]');
        for (var i = 0; i < links.length; i++) {
            var rel = links[i].getAttribute('rel');
            if (rel && rel !== 'nofollow') {
                for (var j = 0; j < BlogEngine.xfnRelationships.length; j++) {
                    if (rel.indexOf(BlogEngine.xfnRelationships[j]) > -1) {
                        links[i].title = 'XFN relationship: ' + rel;
                        break;
                    }
                }
            }
        }
    },

    addLoadEvent: function (func) {
        if (document.readyState === 'complete' || document.readyState === 'interactive') {
            setTimeout(func, 0);
        } else {
            document.addEventListener('DOMContentLoaded', func);
        }
    }
};

BlogEngine.addLoadEvent(BlogEngine.hightLightXfn);
BlogEngine.addLoadEvent(BlogEngine.applyRatings);
