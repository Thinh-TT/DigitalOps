import sqlite3
import pytest
from rag_data_scraper.db.init_sqlite import init_sqlite_db

def test_sqlite_db_init(tmp_path):
    db_file = tmp_path / "test_crawler.db"
    init_sqlite_db(db_file)
    
    assert db_file.exists()
    
    with sqlite3.connect(db_file) as conn:
        cursor = conn.cursor()
        cursor.execute("SELECT name FROM sqlite_master WHERE type='table';")
        tables = {row[0] for row in cursor.fetchall()}
        
    expected_tables = {
        "Sources",
        "CrawledResources",
        "ResourceFetchHistory",
        "CrawlJobs",
        "CrawlFrontier",
    }
    assert expected_tables.issubset(tables)
