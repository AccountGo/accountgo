#!/bin/bash

echo "=== Bootstrap 5 Data Attribute Migration ==="
echo ""

# Find all .cshtml and .razor files
FILES=$(find ./src/AccountGoWeb/Views ./src/AccountGoWeb/Components -type f \( -name "*.cshtml" -o -name "*.razor" \))

# Count occurrences before changes
echo "Before migration:"
echo "  data-toggle: $(grep -r 'data-toggle=' $FILES | wc -l | tr -d ' ')"
echo "  data-target: $(grep -r 'data-target=' $FILES | wc -l | tr -d ' ')"
echo "  data-dismiss: $(grep -r 'data-dismiss=' $FILES | wc -l | tr -d ' ')"
echo ""

# Backup count
BACKED_UP=0

# Process each file
for FILE in $FILES; do
    if grep -q 'data-toggle=\|data-target=\|data-dismiss=' "$FILE" 2>/dev/null; then
        # Create backup
        cp "$FILE" "$FILE.bs4backup"
        BACKED_UP=$((BACKED_UP + 1))
        
        # Replace data-toggle with data-bs-toggle (except data-widget which is CoreUI specific)
        sed -i '' 's/data-toggle="/data-bs-toggle="/g' "$FILE"
        
        # Replace data-target with data-bs-target
        sed -i '' 's/data-target="/data-bs-target="/g' "$FILE"
        
        # Replace data-dismiss with data-bs-dismiss
        sed -i '' 's/data-dismiss="/data-bs-dismiss="/g' "$FILE"
        
        echo "Updated: $FILE"
    fi
done

echo ""
echo "=== Migration Summary ==="
echo "Files backed up: $BACKED_UP"
echo ""
echo "After migration:"
echo "  data-toggle: $(grep -r 'data-toggle=' $FILES | wc -l | tr -d ' ')"
echo "  data-target: $(grep -r 'data-target=' $FILES | wc -l | tr -d ' ')"
echo "  data-dismiss: $(grep -r 'data-dismiss=' $FILES | wc -l | tr -d ' ')"
echo "  data-bs-toggle: $(grep -r 'data-bs-toggle=' $FILES | wc -l | tr -d ' ')"
echo "  data-bs-target: $(grep -r 'data-bs-target=' $FILES | wc -l | tr -d ' ')"
echo "  data-bs-dismiss: $(grep -r 'data-bs-dismiss=' $FILES | wc -l | tr -d ' ')"
echo ""
echo "✅ Bootstrap 5 data attribute migration complete!"
echo "Note: Some data-toggle may remain for CoreUI-specific widgets (data-widget)"
