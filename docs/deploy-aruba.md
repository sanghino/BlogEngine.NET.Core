# Pubblicazione su Aruba (hosting Windows)

Guida passo passo per installare e aggiornare BlogEngine.NET sull'hosting Windows di Aruba usando solo l'FTP.

## Come è organizzato il sito sul server

```
/                 radice FTP
├── public/       il sito (tutto ciò che crea il pacchetto)
└── App_Data/     i dati del blog: post, utenti, file caricati, temi ed estensioni aggiunti, chiavi, log
```

- `public` si sostituisce a ogni aggiornamento.
- `App_Data` **non si sovrascrive e non si cancella mai**: contiene tutto il blog. Il pacchetto non la include apposta.
- Il sito la trova grazie a `appsettings.json` (`"DataPath": "../App_Data"`) e crea da solo le sottocartelle mancanti.

## 1. Preparare il pacchetto (sul PC)

Serve l'**SDK .NET 10**. Va installato una volta sola da https://dotnet.microsoft.com/download/dotnet/10.0, scegliendo "SDK" per Windows x64.

Apri PowerShell nella cartella principale del progetto (quella che contiene `src` e `tools`) ed esegui:

```powershell
.\tools\publish-aruba.ps1
```

Lo script non ha un parametro per scegliere la cartella di uscita: crea sempre `artifacts\aruba\<data-ora>\` dentro il progetto (e lo zip con lo stesso nome), che contiene:

| File | A cosa serve |
|---|---|
| `public\` | il contenuto da caricare nella cartella `public` del sito |
| `app_offline.htm` | la pagina "Aggiornamento in corso" da usare durante l'aggiornamento |
| `LEGGIMI.txt` | le opzioni con cui è stato creato il pacchetto |

Impostazioni predefinite e perché:

- **.NET 10**: il supporto di .NET 8 termina il 10 novembre 2026, quello di .NET 10 a novembre 2028.
- **Self-contained**: il runtime .NET è dentro il pacchetto. Non importa quale versione di .NET sia installata da Aruba. Il pacchetto pesa circa 150 MB.
- **Out-of-process**: il sito gira in un processo proprio. Funziona sia con pool applicativi a 32 bit sia a 64 bit.

Varianti, se servono:

```powershell
# Pacchetto piccolo che usa il .NET 8 già installato da Aruba (8.0.32, modalità "hosted").
# Attenzione: .NET 8 non riceve più aggiornamenti di sicurezza dopo il 10 novembre 2026.
.\tools\publish-aruba.ps1 -Framework net8.0 -Mode FrameworkDependent

# Pacchetto piccolo con .NET 10, quando Aruba lo installerà sul server
.\tools\publish-aruba.ps1 -Mode FrameworkDependent
```

## 2. Prima installazione

Tempo necessario: circa 20-30 minuti, quasi tutti per il caricamento via FTP. Serve: il pacchetto del passo 1, un programma FTP (per esempio FileZilla) con i dati di accesso Aruba e l'accesso al pannello Aruba.

Alla prima installazione il blog è vuoto: l'amministratore, il nome del blog e il tipo di archiviazione si scelgono in una pagina di configurazione che compare da sola al primo avvio.

### Passo 1: controllare il pacchetto sul PC

Nella cartella `artifacts\aruba\<data-ora>\public\` devono esserci direttamente i file del sito, tra cui `web.config` e `BlogEngine.Web.dll` (e `BlogEngine.Web.exe` con il pacchetto predefinito). Se li vedi dentro un'altra sottocartella, hai aperto la cartella sbagliata.

### Passo 2: preparare l'hosting dal pannello Aruba

1. Imposta il pool applicativo del sito su **.NET Core**: di solito è su "Framework .NET 4.0", che non avvia il sito. Tra le modalità scegli **self-contained**, oppure "hosted" se usi il pacchetto `-Mode FrameworkDependent`.
2. Con il programma FTP guarda la radice del tuo spazio: deve esserci la cartella `public` (il sito). Accanto a `public`, **non dentro**, crea la cartella `App_Data` se non c'è già. È qui che il blog salva tutti i suoi dati:

   ```
   /                 radice FTP
   ├── public/       il sito
   └── App_Data/     i dati del blog
   ```

3. Dal pannello Aruba (Pannello di controllo → Hosting → Gestione permessi) dai alla cartella `App_Data` i **permessi di scrittura**. Senza, il sito non riesce a salvare nulla e non parte.
4. Svuota la cartella `public`: cancella la pagina di benvenuto di Aruba (per esempio `index.html`), altrimenti verrebbe mostrata al posto del blog.

### Passo 3: caricare il sito via FTP

1. Apri la cartella `public` del server.
2. Dal PC, apri `artifacts\aruba\<data-ora>\public\`, seleziona **tutto il contenuto** (file e cartelle) e trascinalo dentro `public` sul server. Non caricare la cartella `public` stessa, e non caricare `app_offline.htm` né `LEGGIMI.txt`.
3. Aspetta la fine del trasferimento. Con il pacchetto predefinito sono circa 150 MB, quindi servono diversi minuti: non interromperlo. Se il programma FTP segnala file falliti, riprovali.
4. Controlla che sul server, direttamente dentro `public`, ci siano `web.config` e `BlogEngine.Web.dll`.

### Passo 4: aprire il sito la prima volta

1. Apri nel browser l'indirizzo del blog. Se hai già attivato il certificato SSL, usa `https://`: la password dell'amministratore che inserirai tra poco viaggerà protetta.
2. Il primo caricamento è lento (anche 20-30 secondi): il sito si sta avviando.
3. Deve comparire la pagina **Configurazione iniziale**. Al primo avvio il sito ha creato anche il file `App_Data/setup-key.txt`, la chiave che serve nel passo successivo.
4. Se invece compare un errore (500.30, 502.5, 500.19...) vai alla sezione **6. Se qualcosa non va**. Se `setup-key.txt` non esiste dopo l'apertura del sito, il sito non è partito oppure `App_Data` non ha i permessi di scrittura.

### Passo 5: leggere la chiave di configurazione

La chiave impedisce che qualcun altro configuri il blog al posto tuo.

1. Con il programma FTP apri `App_Data` (accanto a `public`).
2. Scarica `setup-key.txt` sul PC e aprilo con il Blocco note.
3. Copia la stringa che contiene: sono 32 caratteri fatti di cifre e lettere da A a F. Le maiuscole e le minuscole non contano.

### Passo 6: compilare la configurazione iniziale

Torna alla pagina **Configurazione iniziale** nel browser e compila il modulo:

| Campo | Cosa inserire |
|---|---|
| Chiave di configurazione | la stringa copiata da `setup-key.txt` |
| Archiviazione dei dati | **File XML** (file semplici e leggibili, consigliato per blog piccoli) oppure **Database SQLite** (un unico file, più veloce con molti post). Decidilo ora: dopo l'installazione non c'è una voce per cambiarlo. |
| Nome del Blog | il titolo che vedranno i visitatori (si può cambiare poi nelle impostazioni) |
| Nome utente | il nome con cui accedi all'amministrazione |
| Email | il tuo indirizzo email |
| Password | almeno **8 caratteri** |
| Conferma password | la stessa password |

Premi **Installa**. Se qualcosa non va, la pagina mostra l'errore accanto al campo (per esempio "La chiave di configurazione non è valida") e puoi correggerlo e riprovare.

Quando l'installazione riesce, sei già collegato come amministratore e vieni portato alla home del blog. Da questo momento:
- la pagina `/setup` non esiste più (risponde "non trovato");
- il file `setup-key.txt` viene cancellato dal server;
- compare `App_Data/installation.json`: non cancellarlo, è ciò che dice al sito che è già installato.

### Passo 7: controllare che sia tutto a posto

1. Apri `https://tuodominio/admin/` (l'amministrazione) e poi `https://tuodominio/admin/diagnostics` (nel menu: **Informazioni → Diagnostica**). Tutte le cartelle devono avere il segno di spunta verde. Se una ha il triangolo rosso, dai i permessi di scrittura ad `App_Data` dal pannello Aruba (passo 2).
2. Imposta le email (vedi sotto) e prova il pulsante di invio di prova.
3. Attiva il certificato SSL gratuito dal pannello Aruba, se non l'hai ancora fatto (sezione 7).
4. Crea un articolo di prova, aprilo sul sito e poi cancellalo. Per un controllo più completo segui `docs/checklist-collaudo.md`.

### Email (Impostazioni → Email)

Con una casella di posta Aruba:

| Campo | Valore |
|---|---|
| Server SMTP | `smtps.aruba.it` |
| Porta | `465` |
| SSL | attivo |
| Nome utente | l'indirizzo email completo |
| Password | la password della casella |

Con il pulsante di prova invii un messaggio di test.

## 3. Aggiornare il sito

1. **Backup** (vedi sotto).
2. Crea il nuovo pacchetto con `.\tools\publish-aruba.ps1`.
3. Carica `app_offline.htm` dentro `public` sul server. IIS ferma il sito, mostra "Aggiornamento in corso" e sblocca i file.
4. Carica il contenuto di `public\` del pacchetto in `public`, sovrascrivendo i file esistenti.
5. **Cancella** `app_offline.htm` da `public`.
6. Apri il sito e verifica home page, login e un post. Usa la checklist in `docs/checklist-collaudo.md`.

Alcune cartelle non vengono toccate dall'aggiornamento perché stanno in `App_Data`:
- i temi, le estensioni e i widget caricati;
- i file caricati.

Quelli inclusi nel sito, cioè `public/Custom/...`, vengono invece aggiornati.

### Manutenzione più lunga

`app_offline.htm` ferma tutto il sito, amministrazione compresa, e serve solo durante il caricamento dei file. Se vuoi tenere chiuso il blog ai visitatori per più tempo e continuare a lavorarci, usa invece **Impostazioni → Base → Sito in manutenzione**:
- i visitatori vedono la pagina di manutenzione;
- tu, collegato, vedi il sito normalmente, con un avviso giallo in cima.

## 4. Backup

Prima di ogni aggiornamento scarica via FTP sul PC:

- tutta la cartella `App_Data`: sono i dati, ed è la parte più importante;
- tutta la cartella `public`: serve per tornare alla versione precedente.

Mettile in una cartella con la data, ad esempio `backup\2026-10-22\`.

Tra un aggiornamento e l'altro conviene comunque copiare `App_Data` ogni tanto. In alternativa, dall'amministrazione puoi usare **Impostazioni → Avanzate → Esporta**, che salva un file BlogML con post, pagine e commenti.

## 5. Tornare alla versione precedente (rollback)

Se dopo l'aggiornamento il sito non funziona:

1. Carica `app_offline.htm` in `public`.
2. Cancella il contenuto di `public` e ricarica quello del backup.
3. Ripristina `App_Data` dal backup **solo se** l'aggiornamento l'ha modificata. Con SQLite il database viene aggiornato automaticamente all'avvio, quindi ripristinare anche `App_Data` è la scelta sicura.
4. Cancella `app_offline.htm`.

## 6. Se qualcosa non va

| Sintomo | Cosa fare |
|---|---|
| Errore **500.30** o **502.5** (il sito non parte) | In `public/web.config` imposta `stdoutLogEnabled="true"`, ricarica il file e apri il sito. Il motivo è scritto in `App_Data/logs/stdout_*.log`. Poi rimetti `false`. |
| Errore **500.19** | Il modulo ASP.NET Core non è attivo sul piano: chiedi al supporto Aruba di abilitare .NET Core per il sito. |
| Errore all'avvio con il pacchetto `-Mode FrameworkDependent` | Sul server manca la versione di .NET richiesta: usa il pacchetto predefinito (self-contained). |
| Cartelle con il triangolo rosso nella Diagnostica | Dai i permessi di scrittura ad `App_Data` dal pannello Aruba. |
| Le email non partono | Controlla i valori della tabella Email e il registro in **Log** dell'amministrazione. |
| Ho perso la password dell'amministratore | Usa "Password dimenticata" nella pagina di login (serve l'email configurata). |

Gli errori dell'applicazione sono registrati anche in `App_Data/logs` e si leggono dall'amministrazione (**Log**).

## 7. Sicurezza

- Attiva il certificato **SSL gratuito** dal pannello Aruba e usa sempre `https://`. I cookie di accesso viaggiano protetti solo in HTTPS.
- Dopo la configurazione iniziale il file `setup-key.txt` viene cancellato automaticamente e la pagina `/setup` non è più raggiungibile.
- Le estensioni caricate in `App_Data/extensions` sono codice che il sito esegue. Carica solo estensioni di cui ti fidi.
