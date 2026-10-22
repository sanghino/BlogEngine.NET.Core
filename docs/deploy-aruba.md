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

Dalla cartella del progetto, in PowerShell:

```powershell
.\tools\publish-aruba.ps1
```

Lo script crea `artifacts\aruba\<data-ora>\` (e lo zip con lo stesso nome), che contiene:

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

1. Dal pannello Aruba imposta il pool applicativo del sito su **.NET Core**: di solito è su "Framework .NET 4.0", che non avvia il sito. Tra le modalità scegli **self-contained**, o "hosted" se usi il pacchetto `-Mode FrameworkDependent`.
2. Dal pannello Aruba, verifica che la cartella `App_Data` abbia i **permessi di scrittura** (Pannello di controllo → Hosting → Gestione permessi). Se non esiste, creala accanto a `public`.
3. Svuota la cartella `public`: togli la pagina di benvenuto di Aruba.
4. Carica via FTP **il contenuto** di `public\` del pacchetto dentro `public` sul server. Carica i file e le cartelle, non la cartella `public` stessa.
5. Apri il sito nel browser: viene mostrata la pagina **Configurazione iniziale** (`/setup`).
6. Scarica via FTP il file `App_Data/setup-key.txt`, aprilo e copia la chiave nella pagina di configurazione. La chiave impedisce che un estraneo configuri il blog al posto tuo.
7. Scegli il provider dei dati e crea l'utente amministratore:
   - **XML**: file leggibili, consigliato per blog piccoli;
   - **SQLite**: un unico file di database, più veloce con molti contenuti.
8. Accedi a `/admin/` e controlla **Informazioni → Diagnostica**: tutte le cartelle devono avere il segno di spunta verde.

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
