using System.Text.RegularExpressions;
using DigitalOps.API.Features.Attachments;
using DigitalOps.API.Features.IncomingDocuments;
using DigitalOps.API.Features.OutgoingDocuments;
using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace DigitalOps.API.Features.Search;

public sealed class DocumentSearchService(
    DigitalOpsDbContext dbContext) : IDocumentSearchService
{
    public async Task<PagedResponse<DocumentSearchResult>> SearchAsync(
        DocumentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var rawQuery = query.Q?.Trim() ?? string.Empty;
        if (rawQuery.Length < 2)
        {
            throw new ArgumentException("Từ khóa tìm kiếm phải có tối thiểu 2 ký tự.", nameof(query));
        }

        if (query.DateFrom is not null && query.DateTo is not null && query.DateFrom > query.DateTo)
        {
            throw new ArgumentException("Ngày bắt đầu không được sau ngày kết thúc.", nameof(query));
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var terms = rawQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            return new PagedResponse<DocumentSearchResult>([], page, pageSize, 0, 0);
        }

        var documentBestMatches = new Dictionary<Guid, DocumentSearchResult>();

        // 1. Search Incoming Documents
        if ((query.DocumentKind is null or DocumentKind.Incoming)
            && (query.MatchSource is null or DocumentSearchMatchSource.Summary))
        {
            var incomingQuery = dbContext.IncomingDocuments
                .AsNoTracking()
                .Include(d => d.DocumentType)
                .AsQueryable();

            if (query.DocumentTypeId.HasValue)
            {
                incomingQuery = incomingQuery.Where(d => d.DocumentTypeId == query.DocumentTypeId.Value);
            }

            if (query.IncomingStatus.HasValue)
            {
                incomingQuery = incomingQuery.Where(d => d.Status == query.IncomingStatus.Value);
            }

            if (query.DateFrom.HasValue)
            {
                incomingQuery = incomingQuery.Where(d => d.ReceivedDate >= query.DateFrom.Value);
            }

            if (query.DateTo.HasValue)
            {
                incomingQuery = incomingQuery.Where(d => d.ReceivedDate <= query.DateTo.Value);
            }

            if (dbContext.Database.IsNpgsql())
            {
                // The predicate mirrors ix_incoming_documents_summary_fts. SQLite test
                // databases intentionally keep the deterministic in-memory fallback.
                incomingQuery = incomingQuery.Where(d =>
                    EF.Functions.ToTsVector("simple", d.Summary)
                        .Matches(EF.Functions.PlainToTsQuery("simple", rawQuery)));
            }

            var incomingDocs = await incomingQuery.ToListAsync(cancellationToken);

            foreach (var doc in incomingDocs)
            {
                if (query.MatchSource is null or DocumentSearchMatchSource.Summary)
                {
                    var (isMatch, snippet, score) = EvaluateMatch(doc.Summary, rawQuery, terms, baseMultiplier: 0.9);
                    if (isMatch)
                    {
                        var title = !string.IsNullOrWhiteSpace(doc.Summary)
                            ? (doc.Summary.Length > 120 ? doc.Summary[..120] + "..." : doc.Summary)
                            : doc.ReferenceNumber;

                        var result = new DocumentSearchResult(
                            DocumentKind: DocumentKind.Incoming,
                            DocumentId: doc.Id,
                            ReferenceNumber: doc.ReferenceNumber,
                            Title: title,
                            DocumentType: doc.DocumentType.Name,
                            DocumentDate: doc.ReceivedDate,
                            MatchSource: DocumentSearchMatchSource.Summary,
                            Snippet: snippet,
                            Score: score);

                        AddOrUpdateBestMatch(documentBestMatches, result);
                    }
                }
            }
        }

        // 2. Search Outgoing Documents
        if ((query.DocumentKind is null or DocumentKind.Outgoing)
            && (query.MatchSource is null
                or DocumentSearchMatchSource.Title
                or DocumentSearchMatchSource.Content
                or DocumentSearchMatchSource.AiDraftContent))
        {
            var outgoingQuery = dbContext.OutgoingDocuments
                .AsNoTracking()
                .Include(d => d.Template)
                    .ThenInclude(t => t.DocumentType)
                .AsQueryable();

            if (query.DocumentTypeId.HasValue)
            {
                outgoingQuery = outgoingQuery.Where(d => d.Template.DocumentTypeId == query.DocumentTypeId.Value);
            }

            if (query.OutgoingStatus.HasValue)
            {
                outgoingQuery = outgoingQuery.Where(d => d.Status == query.OutgoingStatus.Value);
            }

            if (dbContext.Database.IsNpgsql())
            {
                // This expression mirrors ix_outgoing_documents_content_fts. The
                // source-specific ranking/snippet logic remains unchanged below.
                outgoingQuery = outgoingQuery.Where(d =>
                    EF.Functions.ToTsVector(
                            "simple",
                            (d.Title ?? string.Empty)
                                + " "
                                + (d.Content ?? string.Empty)
                                + " "
                                + (d.AiDraftContent ?? string.Empty))
                        .Matches(EF.Functions.PlainToTsQuery("simple", rawQuery)));
            }

            var outgoingDocs = await outgoingQuery.ToListAsync(cancellationToken);

            foreach (var doc in outgoingDocs)
            {
                var docDate = doc.IssuedDate ?? DateOnly.FromDateTime(doc.CreatedAt);
                if (query.DateFrom.HasValue && docDate < query.DateFrom.Value)
                {
                    continue;
                }

                if (query.DateTo.HasValue && docDate > query.DateTo.Value)
                {
                    continue;
                }

                var docType = doc.Template?.DocumentType?.Name ?? "Văn bản đi";
                var refNumber = doc.ReferenceNumber ?? "(Chưa cấp số)";

                // Check Title match
                if (query.MatchSource is null or DocumentSearchMatchSource.Title)
                {
                    var (isMatch, snippet, score) = EvaluateMatch(doc.Title, rawQuery, terms, baseMultiplier: 1.0);
                    if (isMatch)
                    {
                        var result = new DocumentSearchResult(
                            DocumentKind: DocumentKind.Outgoing,
                            DocumentId: doc.Id,
                            ReferenceNumber: refNumber,
                            Title: doc.Title,
                            DocumentType: docType,
                            DocumentDate: docDate,
                            MatchSource: DocumentSearchMatchSource.Title,
                            Snippet: snippet,
                            Score: score);

                        AddOrUpdateBestMatch(documentBestMatches, result);
                    }
                }

                // Check Content match
                if (query.MatchSource is null or DocumentSearchMatchSource.Content)
                {
                    var (isMatch, snippet, score) = EvaluateMatch(doc.Content, rawQuery, terms, baseMultiplier: 0.85);
                    if (isMatch)
                    {
                        var result = new DocumentSearchResult(
                            DocumentKind: DocumentKind.Outgoing,
                            DocumentId: doc.Id,
                            ReferenceNumber: refNumber,
                            Title: doc.Title,
                            DocumentType: docType,
                            DocumentDate: docDate,
                            MatchSource: DocumentSearchMatchSource.Content,
                            Snippet: snippet,
                            Score: score);

                        AddOrUpdateBestMatch(documentBestMatches, result);
                    }
                }

                // Check AiDraftContent match
                if (query.MatchSource is null or DocumentSearchMatchSource.AiDraftContent)
                {
                    if (!string.IsNullOrWhiteSpace(doc.AiDraftContent))
                    {
                        var (isMatch, snippet, score) = EvaluateMatch(doc.AiDraftContent, rawQuery, terms, baseMultiplier: 0.8);
                        if (isMatch)
                        {
                            var result = new DocumentSearchResult(
                                DocumentKind: DocumentKind.Outgoing,
                                DocumentId: doc.Id,
                                ReferenceNumber: refNumber,
                                Title: doc.Title,
                                DocumentType: docType,
                                DocumentDate: docDate,
                                MatchSource: DocumentSearchMatchSource.AiDraftContent,
                                Snippet: snippet,
                                Score: score);

                            AddOrUpdateBestMatch(documentBestMatches, result);
                        }
                    }
                }
            }
        }

        // 3. Search Attachments (Only ExtractionStatus == Succeeded)
        if (query.MatchSource is null or DocumentSearchMatchSource.Attachment)
        {
            var attachmentsQuery = dbContext.Attachments
                .AsNoTracking()
                .Where(a => a.ExtractionStatus == ExtractionStatus.Succeeded && a.ExtractedText != null && a.ExtractedText != "")
                .Include(a => a.IncomingDocument)
                    .ThenInclude(d => d!.DocumentType)
                .Include(a => a.OutgoingDocument)
                    .ThenInclude(d => d!.Template)
                        .ThenInclude(t => t.DocumentType)
                .AsQueryable();

            if (dbContext.Database.IsNpgsql())
            {
                // Only extracted text in the Succeeded state is searchable. This
                // predicate is backed by ix_attachments_extracted_text_fts.
                attachmentsQuery = attachmentsQuery.Where(a =>
                    EF.Functions.ToTsVector("simple", a.ExtractedText!)
                        .Matches(EF.Functions.PlainToTsQuery("simple", rawQuery)));
            }

            var attachments = await attachmentsQuery.ToListAsync(cancellationToken);

            foreach (var att in attachments)
            {
                var (isMatch, snippet, score) = EvaluateMatch(att.ExtractedText, rawQuery, terms, baseMultiplier: 0.75);
                if (!isMatch)
                {
                    continue;
                }

                // If attachment belongs to IncomingDocument
                if (att.IncomingDocument is not null)
                {
                    if (query.DocumentKind is not null && query.DocumentKind != DocumentKind.Incoming)
                    {
                        continue;
                    }

                    var parent = att.IncomingDocument;
                    if (query.DocumentTypeId.HasValue && parent.DocumentTypeId != query.DocumentTypeId.Value)
                    {
                        continue;
                    }

                    if (query.IncomingStatus.HasValue && parent.Status != query.IncomingStatus.Value)
                    {
                        continue;
                    }

                    if (query.DateFrom.HasValue && parent.ReceivedDate < query.DateFrom.Value)
                    {
                        continue;
                    }

                    if (query.DateTo.HasValue && parent.ReceivedDate > query.DateTo.Value)
                    {
                        continue;
                    }

                    var title = !string.IsNullOrWhiteSpace(parent.Summary)
                        ? (parent.Summary.Length > 120 ? parent.Summary[..120] + "..." : parent.Summary)
                        : parent.ReferenceNumber;

                    var result = new DocumentSearchResult(
                        DocumentKind: DocumentKind.Incoming,
                        DocumentId: parent.Id,
                        ReferenceNumber: parent.ReferenceNumber,
                        Title: title,
                        DocumentType: parent.DocumentType.Name,
                        DocumentDate: parent.ReceivedDate,
                        MatchSource: DocumentSearchMatchSource.Attachment,
                        Snippet: $"[Tệp: {att.FileName}] {snippet}",
                        Score: score);

                    AddOrUpdateBestMatch(documentBestMatches, result);
                }
                // If attachment belongs to OutgoingDocument
                else if (att.OutgoingDocument is not null)
                {
                    if (query.DocumentKind is not null && query.DocumentKind != DocumentKind.Outgoing)
                    {
                        continue;
                    }

                    var parent = att.OutgoingDocument;
                    if (query.DocumentTypeId.HasValue && parent.Template?.DocumentTypeId != query.DocumentTypeId.Value)
                    {
                        continue;
                    }

                    if (query.OutgoingStatus.HasValue && parent.Status != query.OutgoingStatus.Value)
                    {
                        continue;
                    }

                    var docDate = parent.IssuedDate ?? DateOnly.FromDateTime(parent.CreatedAt);
                    if (query.DateFrom.HasValue && docDate < query.DateFrom.Value)
                    {
                        continue;
                    }

                    if (query.DateTo.HasValue && docDate > query.DateTo.Value)
                    {
                        continue;
                    }

                    var docType = parent.Template?.DocumentType?.Name ?? "Văn bản đi";
                    var refNumber = parent.ReferenceNumber ?? "(Chưa cấp số)";

                    var result = new DocumentSearchResult(
                        DocumentKind: DocumentKind.Outgoing,
                        DocumentId: parent.Id,
                        ReferenceNumber: refNumber,
                        Title: parent.Title,
                        DocumentType: docType,
                        DocumentDate: docDate,
                        MatchSource: DocumentSearchMatchSource.Attachment,
                        Snippet: $"[Tệp: {att.FileName}] {snippet}",
                        Score: score);

                    AddOrUpdateBestMatch(documentBestMatches, result);
                }
            }
        }

        // Rank and Paginate
        var allResults = documentBestMatches.Values
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.DocumentDate)
            .ThenBy(r => r.DocumentId)
            .ToList();

        var totalItems = allResults.Count;
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        var pagedItems = allResults.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new PagedResponse<DocumentSearchResult>(
            Items: pagedItems,
            Page: page,
            PageSize: pageSize,
            TotalCount: totalItems,
            TotalPages: totalPages);
    }

    private static void AddOrUpdateBestMatch(
        Dictionary<Guid, DocumentSearchResult> matches,
        DocumentSearchResult candidate)
    {
        if (!matches.TryGetValue(candidate.DocumentId, out var existing) || candidate.Score > existing.Score)
        {
            matches[candidate.DocumentId] = candidate;
        }
    }

    private static (bool IsMatch, string Snippet, double Score) EvaluateMatch(
        string? text,
        string rawQuery,
        string[] terms,
        double baseMultiplier)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (false, string.Empty, 0.0);
        }

        var normalizedText = Regex.Replace(text, @"\s+", " ").Trim();
        var exactIndex = normalizedText.IndexOf(rawQuery, StringComparison.OrdinalIgnoreCase);
        var matchedTermsCount = 0;
        var firstTermIndex = -1;

        foreach (var term in terms)
        {
            var idx = normalizedText.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                matchedTermsCount++;
                if (firstTermIndex == -1 || idx < firstTermIndex)
                {
                    firstTermIndex = idx;
                }
            }
        }

        if (exactIndex < 0 && matchedTermsCount == 0)
        {
            return (false, string.Empty, 0.0);
        }

        // Calculate score
        double score;
        if (exactIndex >= 0)
        {
            score = baseMultiplier * 1.0;
        }
        else
        {
            var coverage = (double)matchedTermsCount / terms.Length;
            score = baseMultiplier * (0.6 + 0.4 * coverage);
        }

        score = Math.Round(Math.Clamp(score, 0.05, 1.0), 2);

        // Generate snippet around match
        var targetIndex = exactIndex >= 0 ? exactIndex : firstTermIndex;
        var snippetStart = Math.Max(0, targetIndex - 40);
        var snippetLength = Math.Min(normalizedText.Length - snippetStart, 160);
        var rawSnippet = normalizedText.Substring(snippetStart, snippetLength);

        // Wrap matches with <b>...</b>
        var highlightedSnippet = rawSnippet;
        foreach (var term in terms.OrderByDescending(t => t.Length))
        {
            highlightedSnippet = Regex.Replace(
                highlightedSnippet,
                Regex.Escape(term),
                m => $"<b>{m.Value}</b>",
                RegexOptions.IgnoreCase);
        }

        var prefix = snippetStart > 0 ? "..." : "";
        var suffix = (snippetStart + snippetLength) < normalizedText.Length ? "..." : "";
        var finalSnippet = $"{prefix}{highlightedSnippet.Trim()}{suffix}";

        return (true, finalSnippet, score);
    }
}
