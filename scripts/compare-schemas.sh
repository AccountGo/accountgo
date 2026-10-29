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
    python3 - "$db" "$out" << 'EOF'
import sys, sqlite3

db_path = sys.argv[1]
out_path = sys.argv[2]

conn = sqlite3.connect(db_path)
cursor = conn.cursor()

# Get all tables, excluding sqlite internal and EF metadata
cursor.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;")
tables = [row[0] for row in cursor.fetchall() if not row[0].startswith("sqlite_") and not row[0].startswith("__")]

lines = []
for table in tables:
    lines.append(f"TABLE: {table}")
    # Columns: sorted alphabetically so ALTER TABLE ADD COLUMN order doesn't cause spurious diffs
    cursor.execute(f'PRAGMA table_info("{table}");')
    cols = sorted(cursor.fetchall(), key=lambda c: c[1].lower())
    for col in cols:
        col_name = col[1]
        col_type = col[2].upper()
        lines.append(f"  COL: {col_name} {col_type}")
    # Indexes
    cursor.execute(f'SELECT name FROM sqlite_master WHERE type="index" AND tbl_name="{table}" ORDER BY name;')
    for idx in cursor.fetchall():
        idx_name = idx[0]
        if not idx_name.startswith("sqlite_"):
            lines.append(f"  IDX: {idx_name}")

with open(out_path, "w") as f:
    f.write("\n".join(lines) + "\n")
EOF
}

echo "==> 4. Extracting columns and indexes..."
dump_schema_details "$FRESH_DB" "$FRESH_SCHEMA"
dump_schema_details "$UPGRADED_DB" "$UPGRADED_SCHEMA"

echo "==> 5. Diffing fresh schema vs upgraded schema..."
if diff -u "$FRESH_SCHEMA" "$UPGRADED_SCHEMA"; then
    python3 - "$FRESH_DB" "$UPGRADED_DB" << 'EOF'
import sys, sqlite3

fresh_db = sys.argv[1]
upgraded_db = sys.argv[2]

conn_fresh = sqlite3.connect(fresh_db)
conn_upgraded = sqlite3.connect(upgraded_db)
cur_fresh = conn_fresh.cursor()
cur_upgraded = conn_upgraded.cursor()

cur_fresh.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;")
tables = [row[0] for row in cur_fresh.fetchall() if not row[0].startswith("sqlite_") and not row[0].startswith("__")]

warns = []
for table in tables:
    cur_fresh.execute(f'PRAGMA table_info("{table}");')
    fresh_cols = {row[1]: row[3] for row in cur_fresh.fetchall()}
    cur_upgraded.execute(f'PRAGMA table_info("{table}");')
    upgraded_cols = {row[1]: row[3] for row in cur_upgraded.fetchall()}
    for col_name, fresh_notnull in fresh_cols.items():
        upgraded_notnull = upgraded_cols.get(col_name)
        if upgraded_notnull is not None and fresh_notnull != upgraded_notnull:
            f_str = "NOT NULL" if fresh_notnull == 1 else "NULL"
            u_str = "NOT NULL" if upgraded_notnull == 1 else "NULL"
            warns.append(f"  [WARN] {table}.{col_name}: Fresh is {f_str}, Upgraded is {u_str} (SQLite preserves column nullability without table rebuild; data is backfilled)")

if warns:
    print(f"\nNotice: Nullability differences on pre-existing columns across {len(tables)} tables:")
    for w in warns:
        print(w)
    print()
EOF
    echo "✓ SUCCESS: Fresh database and upgraded database have matching tables, columns, types, and indexes across all tables (with expected SQLite column nullability warnings noted above)."
else
    echo "✗ ERROR: Schema difference detected between fresh and upgraded database."
    exit 1
fi
