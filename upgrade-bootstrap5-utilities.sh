#!/bin/bash

echo "=== Bootstrap 5 Utility Class Migration ==="
echo ""

# Find all .cshtml and .razor files
FILES=$(find ./src/AccountGoWeb/Views ./src/AccountGoWeb/Components -type f \( -name "*.cshtml" -o -name "*.razor" \))

# Count before
echo "Before migration:"
echo "  ml- (margin-left): $(grep -ro '\bml-' $FILES | wc -l | tr -d ' ')"
echo "  mr- (margin-right): $(grep -ro '\bmr-' $FILES | wc -l | tr -d ' ')"
echo "  pl- (padding-left): $(grep -ro '\bpl-' $FILES | wc -l | tr -d ' ')"
echo "  pr- (padding-right): $(grep -ro '\bpr-' $FILES | wc -l | tr -d ' ')"
echo "  float-left: $(grep -ro 'float-left' $FILES | wc -l | tr -d ' ')"
echo "  float-right: $(grep -ro 'float-right' $FILES | wc -l | tr -d ' ')"
echo ""

UPDATED=0

# Process each file
for FILE in $FILES; do
    CHANGED=0
    
    # Check if any BS4 utilities exist
    if grep -qE '\bml-|\bmr-|\bpl-|\bpr-|float-left|float-right' "$FILE" 2>/dev/null; then
        # Margin utilities: ml- → ms-, mr- → me-
        if sed -i '' 's/\bml-/ms-/g' "$FILE"; then CHANGED=1; fi
        if sed -i '' 's/\bmr-/me-/g' "$FILE"; then CHANGED=1; fi
        
        # Padding utilities: pl- → ps-, pr- → pe-
        if sed -i '' 's/\bpl-/ps-/g' "$FILE"; then CHANGED=1; fi
        if sed -i '' 's/\bpr-/pe-/g' "$FILE"; then CHANGED=1; fi
        
        # Float utilities
        if sed -i '' 's/float-left/float-start/g' "$FILE"; then CHANGED=1; fi
        if sed -i '' 's/float-right/float-end/g' "$FILE"; then CHANGED=1; fi
        
        if [ $CHANGED -eq 1 ]; then
            UPDATED=$((UPDATED + 1))
            echo "Updated: $FILE"
        fi
    fi
done

echo ""
echo "=== Migration Summary ==="
echo "Files updated: $UPDATED"
echo ""
echo "After migration:"
echo "  ms- (margin-start): $(grep -ro '\bms-' $FILES | wc -l | tr -d ' ')"
echo "  me- (margin-end): $(grep -ro '\bme-' $FILES | wc -l | tr -d ' ')"
echo "  ps- (padding-start): $(grep -ro '\bps-' $FILES | wc -l | tr -d ' ')"
echo "  pe- (padding-end): $(grep -ro '\bpe-' $FILES | wc -l | tr -d ' ')"
echo "  float-start: $(grep -ro 'float-start' $FILES | wc -l | tr -d ' ')"
echo "  float-end: $(grep -ro 'float-end' $FILES | wc -l | tr -d ' ')"
echo ""
echo "✅ Bootstrap 5 utility class migration complete!"
