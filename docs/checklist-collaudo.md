# Checklist di collaudo

Da seguire dopo la prima installazione e dopo ogni aggiornamento. Si fa dal browser in circa 20-30 minuti.
Spunta ogni voce. Se qualcosa non va, annota la pagina, cosa hai fatto e il messaggio mostrato.

Conviene usare due browser, oppure una finestra normale e una in incognito: uno per l'amministratore e uno per il visitatore.

## A. Avvio e diagnostica
- [ ] La home page si apre senza errori.
- [ ] `/admin/` → **Informazioni → Diagnostica**: tutte le cartelle hanno la spunta verde e la versione è quella attesa.
- [ ] Logout e nuovo login funzionano (nessun errore 400).

## B. Contenuti
- [ ] Nuovo post con titolo, testo con lettere accentate (è, à, ù), una categoria e due tag: si salva e si pubblica.
- [ ] Nel post: immagine caricata dall'editor, file allegato, video caricato con il pulsante video di TinyMCE.
- [ ] Il post appare in home, nella pagina della categoria, del tag, dell'autore (`/author/nome`) e dell'archivio per data.
- [ ] Gli accenti si vedono correttamente nel post, nel feed RSS (`/syndication.axd`) e nei risultati della ricerca.
- [ ] Post salvato come bozza: non è visibile al visitatore.
- [ ] Post con data futura: compare solo da quella data.
- [ ] Nuova pagina, anche come pagina figlia: si apre dal menu o dal suo indirizzo.
- [ ] Post e pagina eliminati finiscono nel **Cestino** e si possono ripristinare.

## C. Commenti (come visitatore)
- [ ] Commento a un post: con la moderazione attiva resta in attesa.
- [ ] Dall'admin il commento si approva e poi compare nel post.
- [ ] Una risposta a un commento compare annidata.
- [ ] Il voto con le stelle funziona.
- [ ] Un commento con `<script>` viene mostrato come testo, senza eseguire nulla.

## D. Utenti, ruoli e permessi
- [ ] Nuovo utente con ruolo **Editors**: può scrivere i propri post, ma non vede Impostazioni, Utenti e Blog.
- [ ] **Utenti → Ruoli**: concedendo "Crea nuovi blog" a un ruolo, i suoi utenti vedono la voce **Blog**; togliendolo, la voce sparisce.
- [ ] Profilo: nome visualizzato e foto si salvano; il nome visualizzato compare nei post.
- [ ] Registrazione autonoma (se attiva in Impostazioni): il nuovo utente riceve il ruolo iniziale scelto.
- [ ] "Password dimenticata" invia l'email di recupero.

## E. Blog figli
- [ ] **Blog → Nuovo**: il blog figlio si apre al suo indirizzo (es. `/secondo/`).
- [ ] Il blog principale **non** mostra i post del figlio, e viceversa.
- [ ] Il widget Blog List del principale porta al figlio.
- [ ] L'amministratore del figlio accede solo al proprio blog.

## F. Aspetto
- [ ] **Temi**: cambiando tema il sito si aggiorna; tornando al tema originale si ripristina.
- [ ] **Widget**: aggiungere, spostare e configurare un widget (es. Testo, Tag cloud); le modifiche compaiono nel sito.
- [ ] Il sito si legge bene anche dal telefono.

## G. Estensioni
- [ ] **Estensioni**: disattivandone una (es. Smilies) l'effetto sparisce; riattivandola torna.
- [ ] Le impostazioni di un'estensione si salvano. Un campo obbligatorio lasciato vuoto mostra il messaggio nella lingua del blog.

## H. Impostazioni ed email
- [ ] **Impostazioni → Email → Prova**: arriva l'email di prova (con Aruba: `smtps.aruba.it`, porta 465, SSL).
- [ ] Il modulo **Contatti** (`/contact`) invia il messaggio all'indirizzo del blog.
- [ ] Cambiando la **lingua** del blog (Impostazioni → Base) da Italiano a English, l'interfaccia e i messaggi di errore cambiano lingua. Poi rimetti l'italiano.
- [ ] Il **fuso orario** scelto si riflette nell'ora dei post.
- [ ] **Avanzate → Esporta** scarica un file BlogML; **Importa** lo carica in un blog di prova.

## I. Protocolli e indirizzi
- [ ] Il feed `/syndication.axd` si apre in un lettore RSS.
- [ ] `/sitemap.axd` e `/robots.txt` rispondono.
- [ ] Un vecchio indirizzo `.aspx` (es. `/post/titolo.aspx`) porta al post giusto.
- [ ] Facoltativo: Open Live Writer o un'altra app MetaWeblog pubblica un post su `/metaweblog.axd`.

## L. Dopo l'aggiornamento su Aruba
- [ ] `app_offline.htm` è stato cancellato da `public`.
- [ ] Il sito risponde in `https://` con il lucchetto.
- [ ] Il backup di `App_Data` e `public` di questo aggiornamento è salvato sul PC.
