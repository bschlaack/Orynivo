using Lucene.Net.Analysis;
using Lucene.Net.Analysis.De;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using System.IO;
using IOPath = System.IO.Path;
using IODirectory = System.IO.Directory;

namespace Orynivo.Library;

public sealed record SearchResultIds(SearchHitIds Tracks, SearchHitIds Albums, SearchHitIds Artists);

public sealed record SearchHitIds(List<long> Ids, IReadOnlyDictionary<long, float> Scores);

public static class TrackSearchIndex
{
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;
    private const string SchemaVersion = "search-fields-v4";

    private static string Root => AppPaths.GetDataPath("search-index");

    public static bool Exists()
        => IODirectory.Exists(Root) && IODirectory.EnumerateFiles(Root).Any();

    public static bool IsEmpty()
    {
        if (!Exists())
            return true;

        using var directory = FSDirectory.Open(Root);
        if (!DirectoryReader.IndexExists(directory))
            return true;

        using var reader = DirectoryReader.Open(directory);
        return reader.NumDocs == 0;
    }

    public static bool IsCurrent()
    {
        if (IsEmpty())
            return false;

        using var directory = FSDirectory.Open(Root);
        using var reader = DirectoryReader.Open(directory);
        if (reader.MaxDoc == 0)
            return false;

        var doc = reader.Document(0);
        return doc.Get("schema") == SchemaVersion;
    }

    public static void Rebuild(
        IEnumerable<TrackRecord> tracks,
        Action<int, int, string?>? progress = null)
    {
        var trackList = tracks as IReadOnlyList<TrackRecord> ?? tracks.ToList();
        if (IODirectory.Exists(Root))
            IODirectory.Delete(Root, recursive: true);

        IODirectory.CreateDirectory(Root);
        using var directory = FSDirectory.Open(Root);
        using var analyzer = CreateAnalyzer();
        var config = new IndexWriterConfig(Version, analyzer);
        using var writer = new IndexWriter(directory, config);
        for (var index = 0; index < trackList.Count; index++)
        {
            var track = trackList[index];
            writer.AddDocument(ToDocument(track));
            if (progress is not null &&
                (index == trackList.Count - 1 || index % 100 == 0))
            {
                progress(index + 1, trackList.Count, track.FileName);
            }
        }
        writer.Commit();
    }

    public static void UpdateMany(IEnumerable<TrackRecord> tracks)
    {
        IODirectory.CreateDirectory(Root);
        using var directory = FSDirectory.Open(Root);
        using var analyzer = CreateAnalyzer();
        var config = new IndexWriterConfig(Version, analyzer);
        using var writer = new IndexWriter(directory, config);
        foreach (var track in tracks)
            writer.UpdateDocument(new Term("path", track.Path), ToDocument(track));
        writer.Commit();
    }

    public static void RemoveMissingUnderRoot(string rootPath, IEnumerable<string> existingPaths)
    {
        if (!Exists())
            return;

        var existing = new HashSet<string>(existingPaths, StringComparer.OrdinalIgnoreCase);
        using var directory = FSDirectory.Open(Root);
        using var analyzer = CreateAnalyzer();
        var config = new IndexWriterConfig(Version, analyzer);
        using var writer = new IndexWriter(directory, config);
        using var reader = DirectoryReader.Open(directory);
        for (var i = 0; i < reader.MaxDoc; i++)
        {
            var doc = reader.Document(i);
            var path = doc.Get("path");
            if (string.IsNullOrWhiteSpace(path))
                continue;
            if (!path.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                continue;
            if (existing.Contains(path))
                continue;

            writer.DeleteDocuments(new Term("path", path));
        }
        writer.Commit();
    }

    public static List<long> Search(string queryText, int maxResults = 500)
    {
        if (string.IsNullOrWhiteSpace(queryText) || !Exists())
            return [];

        using var directory = FSDirectory.Open(Root);
        using var reader = DirectoryReader.Open(directory);
        var searcher = new IndexSearcher(reader);
        using var analyzer = CreateAnalyzer();
        var query = BuildPartialWordQuery(analyzer, ["all"], queryText);
        if (query is null)
            return [];

        var hits = searcher.Search(query, maxResults).ScoreDocs;
        var result = new List<long>(hits.Length);
        foreach (var hit in hits)
        {
            var doc = searcher.Doc(hit.Doc);
            if (long.TryParse(doc.Get("id"), out var id))
                result.Add(id);
        }
        return result;
    }

    public static SearchResultIds SearchByCategory(string queryText, int maxResults = 500)
    {
        if (string.IsNullOrWhiteSpace(queryText) || !Exists())
            return new SearchResultIds(EmptyHits(), EmptyHits(), EmptyHits());

        using var directory = FSDirectory.Open(Root);
        using var reader = DirectoryReader.Open(directory);
        var searcher = new IndexSearcher(reader);
        using var analyzer = CreateAnalyzer();

        return new SearchResultIds(
            SearchInFields(searcher, analyzer, ["title", "artist", "album"], queryText, maxResults),
            SearchInFields(searcher, analyzer, ["album", "album_artist"], queryText, maxResults),
            SearchInFields(searcher, analyzer, ["artist"], queryText, maxResults));
    }

    private static Document ToDocument(TrackRecord t)
    {
        var doc = new Document
        {
            new StringField("id", t.Id.ToString(), Field.Store.YES),
            new StringField("path", t.Path, Field.Store.YES),
            new StringField("schema", SchemaVersion, Field.Store.YES),
            new TextField("title", BuildTitleText(t), Field.Store.NO),
            new TextField("album", BuildAlbumText(t), Field.Store.NO),
            new TextField("artist", BuildArtistText(t), Field.Store.NO),
            new TextField("album_artist", BuildAlbumArtistText(t), Field.Store.NO),
            new TextField("all", BuildAllText(t), Field.Store.NO)
        };
        return doc;
    }

    private static SearchHitIds SearchInFields(
        IndexSearcher searcher,
        Analyzer analyzer,
        string[] fieldNames,
        string queryText,
        int maxResults)
    {
        var query = BuildPartialWordQuery(analyzer, fieldNames, queryText);
        if (query is null)
            return EmptyHits();

        var hits = searcher.Search(query, maxResults).ScoreDocs;
        var result = new List<long>(hits.Length);
        var scores = new Dictionary<long, float>();
        foreach (var hit in hits)
        {
            var doc = searcher.Doc(hit.Doc);
            if (long.TryParse(doc.Get("id"), out var id))
            {
                result.Add(id);
                scores[id] = hit.Score;
            }
        }
        return new SearchHitIds(result, scores);
    }

    private static SearchHitIds EmptyHits()
        => new([], new Dictionary<long, float>());

    private static Analyzer CreateAnalyzer()
        => new GermanAnalyzer(Version);

    private static Query? BuildPartialWordQuery(Analyzer analyzer, string[] fieldNames, string queryText)
    {
        var outer = new BooleanQuery();
        foreach (var fieldName in fieldNames)
        {
            var terms = AnalyzeTerms(analyzer, fieldName, ExpandGermanUmlautVariants(queryText));
            if (terms.Count == 0)
                continue;

            var fieldQuery = new BooleanQuery();
            foreach (var term in terms)
                fieldQuery.Add(new WildcardQuery(new Term(fieldName, $"*{term}*")), Occur.MUST);

            outer.Add(fieldQuery, Occur.SHOULD);
        }

        return outer.Clauses.Count == 0 ? null : outer;
    }

    private static List<string> AnalyzeTerms(Analyzer analyzer, string fieldName, string text)
    {
        var result = new List<string>();
        using var reader = new StringReader(text);
        using var stream = analyzer.GetTokenStream(fieldName, reader);
        var termAttribute = stream.AddAttribute<ICharTermAttribute>();
        stream.Reset();
        while (stream.IncrementToken())
        {
            var term = termAttribute.ToString();
            if (!string.IsNullOrWhiteSpace(term))
                result.Add(term);
        }
        stream.End();
        return result.Distinct(StringComparer.Ordinal).ToList();
    }

    private static string ExpandGermanUmlautVariants(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var expanded = value
            .Replace("Ä", "Ae", StringComparison.Ordinal)
            .Replace("Ö", "Oe", StringComparison.Ordinal)
            .Replace("Ü", "Ue", StringComparison.Ordinal)
            .Replace("ä", "ae", StringComparison.Ordinal)
            .Replace("ö", "oe", StringComparison.Ordinal)
            .Replace("ü", "ue", StringComparison.Ordinal)
            .Replace("ß", "ss", StringComparison.Ordinal);
        var collapsed = value
            .Replace("Ae", "Ä", StringComparison.Ordinal)
            .Replace("Oe", "Ö", StringComparison.Ordinal)
            .Replace("Ue", "Ü", StringComparison.Ordinal)
            .Replace("ae", "ä", StringComparison.Ordinal)
            .Replace("oe", "ö", StringComparison.Ordinal)
            .Replace("ue", "ü", StringComparison.Ordinal)
            .Replace("ss", "ß", StringComparison.Ordinal);

        return $"{value} {expanded} {collapsed}";
    }

    private static string BuildTitleText(TrackRecord t)
        => ExpandGermanUmlautVariants(string.Join(' ', new object?[] { t.Title, t.SortTitle }
            .Where(v => v is not null)));

    private static string BuildAlbumText(TrackRecord t)
        => ExpandGermanUmlautVariants(string.Join(' ', new object?[] { t.Album, t.SortAlbum }
            .Where(v => v is not null)));

    private static string BuildArtistText(TrackRecord t)
        => ExpandGermanUmlautVariants(string.Join(' ', new object?[] { t.Artist, t.SortArtist }
            .Where(v => v is not null)));

    private static string BuildAlbumArtistText(TrackRecord t)
        => ExpandGermanUmlautVariants(string.Join(' ', new object?[] { t.AlbumArtist, t.SortAlbumArtist }
            .Where(v => v is not null)));

    private static string BuildAllText(TrackRecord t)
    {
        var values = new object?[]
        {
            t.Path, t.FileName, t.FileSize, t.ModifiedAt, t.AddedAt,
            t.Format, t.Duration, t.SampleRate, t.BitDepth, t.Channels, t.Bitrate,
            t.IsLossless, t.IsDsd, t.DsdRate, t.Title, t.SortTitle, t.Artist, t.SortArtist,
            t.AlbumArtist, t.SortAlbumArtist, t.Album, t.SortAlbum, t.Genre, t.Year, t.Date,
            t.TrackNumber, t.TrackTotal, t.DiscNumber, t.DiscTotal, t.Composer, t.Conductor,
            t.Lyricist, t.Lyrics, t.Comment, t.Copyright, t.Publisher, t.EncodedBy,
            t.EncodingSettings, t.Bpm, t.Compilation, t.Isrc, t.Language, t.Mood,
            t.ReplayGainTrack, t.ReplayGainAlbum, t.MusicBrainzTrackId, t.MusicBrainzReleaseId,
            t.MusicBrainzArtistId, t.AcoustIdFingerprint, t.HasCover, t.CoverMimeType
        };
        return ExpandGermanUmlautVariants(string.Join(' ', values.Where(v => v is not null)));
    }
}
