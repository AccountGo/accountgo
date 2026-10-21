#!/bin/bash
# scripts/compare-schemas.sh
# Boots the API on an empty database and on an upgraded database copy,
# then diffs the columns and indexes of every table.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

FRESH_DB="/tmp/accountgo_fresh_$$.db"
UPGRADED_DB="/tmp/accountgo_upgraded_$$.db"
FRESH_SCHEMA="/tmp/fresh_schema_$$.txt"
UPGRADED_SCHEMA="/tmp/upgraded_schema_$$.txt"

cleanup() {
    rm -f "$FRESH_DB" "$UPGRADED_DB" "$FRESH_SCHEMA" "$UPGRADED_SCHEMA"
}
trap cleanup EXIT

echo "==> 1. Preparing database files..."
rm -f "$FRESH_DB" "$UPGRADED_DB"
cp "$REPO_ROOT/src/Api/accountgo.db" "$UPGRADED_DB"

echo "==> 2. Booting API on empty database to generate fresh EF schema..."
python3 -c "
import subprocess, time, os

env = os.environ.copy()
env['ConnectionStrings__SqliteConnection'] = 'Data Source=$FRESH_DB'
env['ENVIRONMENT'] = 'Development'
env['SECRET_KEY'] = '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef'

proc = subprocess.Popen(['dotnet', 'run', '--project', '$REPO_ROOT/src/Api/Api.csproj', '--no-build', '--no-launch-profile', '--urls', 'http://127.0.0.1:8996'],
                        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, env=env)

start = time.time()
while time.time() - start < 30:
    line = proc.stdout.readline()
    if 'Now listening on' in line:
        break
    if proc.poll() is not None:
        break

proc.terminate()
try:
    proc.wait(timeout=5)
except:
    proc.kill()
"

echo "==> 3. Booting API on existing database copy to run upgrader..."
python3 -c "
import subprocess, time, os

env = os.environ.copy()
env['ConnectionStrings__SqliteConnection'] = 'Data Source=$UPGRADED_DB'
env['ENVIRONMENT'] = 'Production'
env['SECRET_KEY'] = '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef'

proc = subprocess.Popen(['dotnet', 'run', '--project', '$REPO_ROOT/src/Api/Api.csproj', '--no-build', '--no-launch-profile', '--urls', 'http://127.0.0.1:8997'],
                        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, env=env)

start = time.time()
while time.time() - start < 30:
    line = proc.stdout.readline()
    if 'Now listening on' in line:
        break
    if proc.poll() is not None:
        break

proc.terminate()
try:
    proc.wait(timeout=5)
except:
    proc.kill()
"

dump_schema_details() {
    local db="$1"
    local out="$2"
    python3 -c "
import sqlite3

conn = sqlite3.connect('$db')
cursor = conn.cursor()

# Get all tables
cursor.execute(\"SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__%' ORDER BY name;\")
tables = [row[0] for row in cursor.fetchall()]

lines = []
for table in tables:
    lines.append(f'TABLE: {table}')
    # Columns: cid, name, type, notnull, dflt_value, pk
    cursor.execute(f'PRAGMA table_info(\"{table}\");')
    for col in cursor.fetchall():
        col_name = col[1]
        col_type = col[2].upper()
        lines.append(f'  COL: {col_name} {col_type}')
    # Indexes
    cursor.execute(f\"SELECT name, sql FROM sqlite_master WHERE type='index' AND tbl_name='{table}' AND name NOT LIKE 'sqlite_%' ORDER BY name;\")
    for idx in cursor.fetchall():
        idx_name = idx[0]
        lines.append(f'  IDX: {idx_name}')

with open('$out', 'w') as f:
    f.write('\n'.join(lines) + '\n')
"
}

echo "==> 4. Extracting columns and indexes..."
dump_schema_details "$FRESH_DB" "$FRESH_SCHEMA"
dump_schema_details "$UPGRADED_DB" "$UPGRADED_SCHEMA"

echo "==> 5. Diffing fresh schema vs upgraded schema..."
if diff -u "$FRESH_SCHEMA" "$UPGRADED_SCHEMA"; then
    echo "✓ SUCCESS: Fresh database and upgraded database have identical columns and indexes across all tables!"
else
    echo "✗ ERROR: Schema difference detected between fresh and upgraded database."
    exit 1
fi
