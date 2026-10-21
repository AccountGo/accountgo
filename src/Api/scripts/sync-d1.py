#!/usr/bin/env python3
"""
Syncs the local SQLite database (accountgo.db) to Cloudflare D1 (accountgo-db).
Run this command only when database schema or seed data updates need to be migrated to remote D1:
  python3 src/Api/scripts/sync-d1.py
"""

import os
import sys
import sqlite3
import tempfile
import subprocess

API_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DB_PATH = os.path.join(API_DIR, "accountgo.db")
DB_NAME = "accountgo-db"

if not os.path.exists(DB_PATH):
    print(f"Error: Database file not found at {DB_PATH}")
    sys.exit(1)

print(f"Reading local database: {DB_PATH}")
conn = sqlite3.connect(DB_PATH)
c = conn.cursor()

c.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name != '_cf_KV';")
tables = [r[0] for r in c.fetchall()]

with tempfile.NamedTemporaryFile("w", suffix=".sql", delete=False) as tmp_file:
    sql_path = tmp_file.name
    tmp_file.write("PRAGMA foreign_keys = OFF;\n")
    
    total_statements = 0
    for t in tables:
        c.execute(f'SELECT count(*) FROM "{t}";')
        cnt = c.fetchone()[0]
        if cnt == 0:
            continue
            
        c.execute(f'PRAGMA table_info("{t}");')
        cols = [f'"{col[1]}"' for col in c.fetchall()]
        cols_str = ', '.join(cols)
        
        c.execute(f'SELECT * FROM "{t}";')
        rows = c.fetchall()
        for r in rows:
            vals = []
            for v in r:
                if v is None:
                    vals.append('NULL')
                elif isinstance(v, (int, float)):
                    vals.append(str(v))
                else:
                    escaped = str(v).replace("'", "''")
                    vals.append(f"'{escaped}'")
            vals_str = ', '.join(vals)
            tmp_file.write(f'INSERT OR REPLACE INTO "{t}" ({cols_str}) VALUES ({vals_str});\n')
            total_statements += 1

print(f"Generated {total_statements} migration statements across {len(tables)} tables.")
print(f"Executing migration to Cloudflare D1 ({DB_NAME})...")

cmd = ["pnpm", "exec", "wrangler", "d1", "execute", DB_NAME, "--remote", f"--file={sql_path}", "--yes"]
try:
    res = subprocess.run(cmd, cwd=API_DIR, check=True)
    print("✓ Successfully synchronized local changes to Cloudflare D1.")
except subprocess.CalledProcessError as e:
    print(f"Error executing wrangler D1 migration: {e}")
    sys.exit(e.returncode)
finally:
    if os.path.exists(sql_path):
        os.remove(sql_path)
