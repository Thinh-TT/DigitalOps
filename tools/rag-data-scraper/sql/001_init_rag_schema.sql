-- PostgreSQL System of Record Schema for DigitalOps Multi-source RAG
-- Script: 001_init_rag_schema.sql

BEGIN;

CREATE TABLE IF NOT EXISTS rag_documents (
    document_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    authority_namespace VARCHAR(128),
    canonical_document_key VARCHAR(256) NOT NULL UNIQUE,
    document_identity_strategy VARCHAR(32) NOT NULL CHECK (document_identity_strategy IN ('authoritative', 'canonical_metadata', 'source_scoped', 'content_only')),
    title VARCHAR(512) NOT NULL,
    active_version_id UUID,
    active_chunk_set_id UUID,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS rag_document_versions (
    version_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID NOT NULL REFERENCES rag_documents(document_id) ON DELETE CASCADE,
    raw_artifact_uri VARCHAR(1024) NOT NULL,
    raw_sha256 CHAR(64) NOT NULL,
    mime_type VARCHAR(128) NOT NULL,
    normalized_text_uri VARCHAR(1024) NOT NULL,
    normalized_text_sha256 CHAR(64) NOT NULL,
    char_count INT NOT NULL CHECK (char_count >= 0),
    word_count INT NOT NULL CHECK (word_count >= 0),
    extraction_quality JSONB NOT NULL,
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS rag_document_sources (
    source_mapping_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID NOT NULL REFERENCES rag_documents(document_id) ON DELETE CASCADE,
    version_id UUID NOT NULL REFERENCES rag_document_versions(version_id) ON DELETE CASCADE,
    source_id VARCHAR(128) NOT NULL,
    source_namespace VARCHAR(128) NOT NULL,
    source_document_url VARCHAR(1024) NOT NULL,
    crawled_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS rag_chunk_sets (
    chunk_set_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    version_id UUID NOT NULL REFERENCES rag_document_versions(version_id) ON DELETE CASCADE,
    chunking_strategy VARCHAR(64) NOT NULL,
    chunker_version VARCHAR(32) NOT NULL,
    tokenizer_name VARCHAR(128) NOT NULL,
    target_tokens INT NOT NULL CHECK (target_tokens > 0),
    overlap_tokens INT NOT NULL CHECK (overlap_tokens >= 0),
    total_chunks INT NOT NULL CHECK (total_chunks >= 0),
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS rag_chunks (
    chunk_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    chunk_set_id UUID NOT NULL REFERENCES rag_chunk_sets(chunk_set_id) ON DELETE CASCADE,
    chunk_index INT NOT NULL CHECK (chunk_index >= 0),
    text TEXT NOT NULL,
    token_count INT NOT NULL CHECK (token_count > 0),
    character_start INT NOT NULL CHECK (character_start >= 0),
    character_end INT NOT NULL CHECK (character_end >= 0),
    content_sha256 CHAR(64) NOT NULL,
    heading_path TEXT,
    page_numbers INT[] DEFAULT '{}',
    structure_metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    allowed_roles VARCHAR(64)[] NOT NULL DEFAULT '{"public"}',
    denied_roles VARCHAR(64)[] NOT NULL DEFAULT '{}',
    security_classification VARCHAR(32) NOT NULL DEFAULT 'internal',
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS rag_index_generations (
    index_generation_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    collection_name VARCHAR(128) NOT NULL,
    embedding_model VARCHAR(128) NOT NULL,
    embedding_dimension INT NOT NULL CHECK (embedding_dimension > 0),
    distance_metric VARCHAR(32) NOT NULL,
    status VARCHAR(32) NOT NULL CHECK (status IN ('building', 'active', 'retaining', 'deprecated', 'failed')),
    activated_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS rag_index_points (
    point_id UUID PRIMARY KEY,
    index_generation_id UUID NOT NULL REFERENCES rag_index_generations(index_generation_id) ON DELETE CASCADE,
    chunk_id UUID NOT NULL REFERENCES rag_chunks(chunk_id) ON DELETE CASCADE,
    chunk_set_id UUID NOT NULL REFERENCES rag_chunk_sets(chunk_set_id) ON DELETE CASCADE,
    version_id UUID NOT NULL REFERENCES rag_document_versions(version_id) ON DELETE CASCADE,
    document_id UUID NOT NULL REFERENCES rag_documents(document_id) ON DELETE CASCADE,
    qdrant_point_id UUID NOT NULL UNIQUE,
    status VARCHAR(32) NOT NULL CHECK (status IN ('pending', 'indexed', 'failed', 'deleted')),
    error_message TEXT,
    indexed_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS rag_citation_snapshots (
    snapshot_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    business_entity_type VARCHAR(128) NOT NULL,
    business_entity_id UUID NOT NULL,
    query_text TEXT NOT NULL,
    retrieved_chunk_ids UUID[] NOT NULL,
    citation_payload JSONB NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS rag_ingestion_jobs (
    job_id VARCHAR(128) PRIMARY KEY,
    staging_directory VARCHAR(1024) NOT NULL,
    status VARCHAR(32) NOT NULL CHECK (status IN ('pending', 'validating', 'processing', 'completed', 'failed')),
    total_observations INT DEFAULT 0,
    processed_observations INT DEFAULT 0,
    failed_observations INT DEFAULT 0,
    started_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    completed_at TIMESTAMPTZ,
    error_summary TEXT
);

CREATE TABLE IF NOT EXISTS rag_ingestion_errors (
    error_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    job_id VARCHAR(128) NOT NULL REFERENCES rag_ingestion_jobs(job_id) ON DELETE CASCADE,
    stage VARCHAR(32) NOT NULL,
    entity_type VARCHAR(64) NOT NULL,
    entity_id VARCHAR(256),
    error_message TEXT NOT NULL,
    stack_trace TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Foreign key constraints for active pointers on rag_documents
ALTER TABLE rag_documents
    ADD CONSTRAINT fk_rag_docs_active_version
    FOREIGN KEY (active_version_id) REFERENCES rag_document_versions(version_id) ON DELETE SET NULL;

ALTER TABLE rag_documents
    ADD CONSTRAINT fk_rag_docs_active_chunk_set
    FOREIGN KEY (active_chunk_set_id) REFERENCES rag_chunk_sets(chunk_set_id) ON DELETE SET NULL;

-- Indexes
CREATE INDEX IF NOT EXISTS idx_rag_docs_canonical_key ON rag_documents(canonical_document_key);
CREATE INDEX IF NOT EXISTS idx_rag_doc_versions_doc_id ON rag_document_versions(document_id);
CREATE INDEX IF NOT EXISTS idx_rag_doc_sources_doc_id ON rag_document_sources(document_id);
CREATE INDEX IF NOT EXISTS idx_rag_doc_sources_version_id ON rag_document_sources(version_id);
CREATE INDEX IF NOT EXISTS idx_rag_chunk_sets_version_id ON rag_chunk_sets(version_id);
CREATE INDEX IF NOT EXISTS idx_rag_chunks_chunk_set_id ON rag_chunks(chunk_set_id);
CREATE INDEX IF NOT EXISTS idx_rag_index_points_gen_chunk ON rag_index_points(index_generation_id, chunk_id);
CREATE INDEX IF NOT EXISTS idx_rag_index_points_doc_id ON rag_index_points(document_id);
CREATE INDEX IF NOT EXISTS idx_rag_citation_snapshots_entity ON rag_citation_snapshots(business_entity_type, business_entity_id);

-- Deferred Constraint Trigger Function for Active Pointer Validation
CREATE OR REPLACE FUNCTION fn_validate_active_pointer_status()
RETURNS TRIGGER AS $$
BEGIN
    IF NEW.active_version_id IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1 FROM rag_document_versions
            WHERE version_id = NEW.active_version_id AND document_id = NEW.document_id
        ) THEN
            RAISE EXCEPTION 'active_version_id % does not belong to document %', NEW.active_version_id, NEW.document_id;
        END IF;
    END IF;

    IF NEW.active_chunk_set_id IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1 FROM rag_chunk_sets cs
            JOIN rag_document_versions dv ON cs.version_id = dv.version_id
            WHERE cs.chunk_set_id = NEW.active_chunk_set_id AND dv.document_id = NEW.document_id
        ) THEN
            RAISE EXCEPTION 'active_chunk_set_id % does not belong to document %', NEW.active_chunk_set_id, NEW.document_id;
        END IF;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_validate_active_pointers ON rag_documents;

CREATE CONSTRAINT TRIGGER trg_validate_active_pointers
AFTER INSERT OR UPDATE ON rag_documents
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW
EXECUTE FUNCTION fn_validate_active_pointer_status();

COMMIT;
