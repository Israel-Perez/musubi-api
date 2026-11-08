using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Musubi.Api.Platform.Moderation;

/// <summary>
/// Moderacion de texto local (02-07): groserias, insultos, odio, contenido sexual, amenazas y venta de drogas en
/// espanol de Mexico (y algunas en ingles). Sin costo ni llamadas a la nube: el plan gratuito de AWS no incluye
/// Translate ni Comprehend (<c>SubscriptionRequiredException</c>, 08-Nov-2026).
/// </summary>
/// <remarks>
/// Compara palabras completas, nunca subcadenas: "computadora" y "disputa" no contienen la palabra "puta".
/// Antes de comparar normaliza lo que se usa para esquivar un filtro:
/// mayusculas, acentos, letras repetidas ("pendejooo"), numeros y simbolos por letras ("p3nd3j0", "put@") y
/// letras sueltas ("p u t a", "p.u.t.a"). Conserva la enie: "cono" (de transito) no es "cono".
/// </remarks>
internal sealed class SpanishLexicon
{
    // Palabras exactas (ya normalizadas al construir). Solo las que en un marketplace de campus casi nunca son
    // inocentes: se dejan fuera "perra", "zorra", "pito", "coger", "pinche" o "huevon" por sus usos comunes.
    private static readonly string[] DefaultWords =
    [
        // Groserias e insultos.
        "puta", "putas", "puto", "putos", "putita", "putito", "putazo", "putazos", "putiza", "putero", "puteria",
        "verga", "vergas", "vergazo", "vergazos", "vergota", "alv",
        "cabron", "cabrona", "cabrones", "cabronas", "cabronada",
        "mierda", "mierdas", "coño", "carajo", "jode", "jodes", "jodas",
        "culo", "culos", "ojete", "ojetes",
        "gilipollas", "malparido", "malparida", "hijueputa", "hdp", "hdtpm", "ctm", "ptm",
        "idiota", "idiotas", "imbecil", "imbeciles", "estupido", "estupida", "estupidos", "estupidas",
        "muerete", "muéranse",

        // Odio.
        "marica", "maricas", "joto", "jotos", "sidoso", "sidosa",

        // Sexual.
        "porno", "nudes", "tetas", "chichis", "mamada", "mamadas",

        // Venta de drogas.
        "cocaina", "marihuana", "mariguana", "metanfetamina", "heroina", "fentanilo",

        // Ingles.
        "shit", "shitty", "asshole", "assholes", "nigga", "niggas", "faggot", "faggots", "cunt", "whore",
    ];

    // Raices: rechazan cualquier palabra que empiece asi. Solo raices sin palabras inocentes conocidas
    // ("verg" queda fuera por "verguenza"; "put" por "putativo"; "nigger" porque al quitar letras repetidas
    // queda "niger", el pais).
    private static readonly string[] DefaultStems =
    [
        "pendej", "ching", "culer", "mierd", "jodid", "joder", "jodete", "maricon", "pornogra", "cabronaz",
        "fuck", "bitch", "motherf",
    ];

    // Frases (amenazas). Se buscan como palabras seguidas, ya normalizadas.
    private static readonly string[] DefaultPhrases =
    [
        "te voy a matar", "te mato", "los voy a matar", "te voy a partir la madre", "te parto la madre",
        "te rompo la madre", "te voy a romper la madre", "ojala te mueras", "hijo de perra",
    ];

    private readonly FrozenSet<string> words;
    private readonly string[] stems;
    private readonly string[] phrases;

    /// <summary>Inicializa el lexico base mas los terminos extra de la configuracion.</summary>
    /// <param name="extraTerms">Palabras o frases extra (<c>Moderation:ExtraBlockedTerms</c>); una frase lleva espacios.</param>
    public SpanishLexicon(IEnumerable<string>? extraTerms = null)
    {
        var extra = (extraTerms ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(Normalize).Where(t => t.Length > 0).ToList();
        words = DefaultWords.Select(Normalize).Concat(extra.Where(t => !t.Contains(' ', StringComparison.Ordinal))).ToFrozenSet(StringComparer.Ordinal);
        stems = [.. DefaultStems.Select(Normalize)];
        phrases = [.. DefaultPhrases.Select(Normalize).Concat(extra.Where(t => t.Contains(' ', StringComparison.Ordinal))).Select(p => $" {p} ")];
    }

    /// <summary>El lexico sin terminos extra.</summary>
    public static SpanishLexicon Default { get; } = new();

    /// <summary>Indica si el texto tiene una palabra, raiz o frase prohibida.</summary>
    /// <param name="text">Texto a revisar (los vacios se permiten).</param>
    /// <returns><c>true</c> si se rechaza.</returns>
    public bool IsOffensive(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var tokens = Tokens(text).ToList();
        if (tokens.Exists(t => IsForbidden(t.Text) || (t.FromLetters && ContainsForbidden(t.Text))))
        {
            return true;
        }

        var padded = $" {string.Join(' ', tokens.Select(t => t.Text))} ";
        return Array.Exists(phrases, p => padded.Contains(p, StringComparison.Ordinal));
    }

    /// <summary>
    /// Minusculas sin acentos (conserva la enie), simbolos por letras dentro de una palabra, letras repetidas
    /// una sola vez y letras sueltas seguidas unidas en una palabra. Las palabras quedan separadas por un espacio.
    /// </summary>
    /// <param name="text">Texto original.</param>
    /// <returns>El texto normalizado.</returns>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Join(' ', Tokens(text).Select(t => t.Text));
    }

    private static IEnumerable<Token> Tokens(string text)
    {
        var tokens = Tokenize(RemoveAccents(text.ToLowerInvariant()))
            .Select(t => t.Any(char.IsLetter) ? ReplaceLeet(t) : t)
            .Select(CollapseRepeats);
        return JoinSingleLetters(tokens);
    }

    private bool IsForbidden(string token) =>
        words.Contains(token) || Array.Exists(stems, s => token.StartsWith(s, StringComparison.Ordinal));

    // Letras sueltas unidas ("p u t a o no" -> "putao"): la palabra puede estar dentro. Solo palabras de 4 letras
    // o mas, para que una sigla deletreada no coincida por azar con "hdp" o "ctm".
    private bool ContainsForbidden(string run) =>
        words.Any(w => w.Length >= 4 && run.Contains(w, StringComparison.Ordinal))
        || Array.Exists(stems, s => run.Contains(s, StringComparison.Ordinal));

    private static string RemoveAccents(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (c == '̃' && builder.Length > 0 && builder[^1] == 'n')
            {
                builder[^1] = 'ñ';
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static bool IsTokenChar(char c) => char.IsLetterOrDigit(c) || c is '@' or '$';

    private static IEnumerable<string> Tokenize(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var inside = i < text.Length && IsTokenChar(text[i]);
            if (inside && start < 0)
            {
                start = i;
            }
            else if (!inside && start >= 0)
            {
                yield return text[start..i];
                start = -1;
            }
        }
    }

    private static string ReplaceLeet(string token) => string.Create(token.Length, token, static (span, source) =>
    {
        for (var i = 0; i < source.Length; i++)
        {
            span[i] = source[i] switch
            {
                '0' => 'o',
                '1' => 'i',
                '3' => 'e',
                '4' or '@' => 'a',
                '5' or '$' => 's',
                '7' => 't',
                _ => source[i],
            };
        }
    });

    private static string CollapseRepeats(string token)
    {
        var builder = new StringBuilder(token.Length);
        foreach (var c in token)
        {
            if (builder.Length == 0 || builder[^1] != c)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    // "p u t a" -> "puta": dos o mas letras sueltas seguidas forman una palabra.
    private static IEnumerable<Token> JoinSingleLetters(IEnumerable<string> tokens)
    {
        var run = new StringBuilder();
        foreach (var token in tokens)
        {
            if (token.Length == 1 && char.IsLetter(token[0]))
            {
                run.Append(token);
                continue;
            }

            if (run.Length > 0)
            {
                yield return new Token(run.ToString(), run.Length > 1);
                run.Clear();
            }

            yield return new Token(token, false);
        }

        if (run.Length > 0)
        {
            yield return new Token(run.ToString(), run.Length > 1);
        }
    }

    /// <summary>Una palabra normalizada.</summary>
    /// <param name="Text">La palabra.</param>
    /// <param name="FromLetters">Si salio de unir letras sueltas.</param>
    private readonly record struct Token(string Text, bool FromLetters);
}
