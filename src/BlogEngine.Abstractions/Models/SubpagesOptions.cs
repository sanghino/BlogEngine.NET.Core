namespace BlogEngine.Models;

/// <summary>Dove compare l'elenco delle sottopagine.</summary>
public enum SubpagesPosition
{
    /// <summary>In fondo alla pagina, oppure dove c'è il segnaposto [sottopagine] (com'era sempre stato).</summary>
    Bottom,

    /// <summary>Solo dove c'è il segnaposto: senza segnaposto i link non compaiono (si scrivono a mano).</summary>
    PlaceholderOnly,
}

/// <summary>Aspetto dell'elenco delle sottopagine.</summary>
public enum SubpagesStyle
{
    /// <summary>Elenco puntato con i titoli (com'era sempre stato).</summary>
    List,

    Numbered,

    /// <summary>Elenco con la descrizione di ogni pagina sotto il titolo.</summary>
    Descriptions,

    /// <summary>Schede affiancate con titolo, descrizione ed eventualmente un'immagine.</summary>
    Cards,

    Buttons,

    /// <summary>Link sulla stessa riga separati da "·".</summary>
    Inline,
}

/// <summary>Ordine delle sottopagine.</summary>
public enum SubpagesOrder
{
    /// <summary>Il campo "ordine" delle pagine figlie (com'era sempre stato).</summary>
    Manual,

    Title,

    /// <summary>Dalla più recente.</summary>
    Newest,
}
