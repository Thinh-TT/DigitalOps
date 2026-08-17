class RagExportError(Exception):
    """Base exception for RAG export failures."""


class InvalidStagingPackageError(RagExportError):
    """Raised when a staging package fails contract or integrity checks."""


class ExportTooLargeError(RagExportError):
    """Raised when a package or generated export exceeds its byte budget."""


class ExportDependencyUnavailableError(RagExportError):
    """Raised when an optional document writer dependency is unavailable."""

