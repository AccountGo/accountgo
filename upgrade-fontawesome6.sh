#!/bin/bash

echo "=== Font Awesome 6 Class Migration ==="
echo ""

# Find all .cshtml and .razor files
FILES=$(find ./src/AccountGoWeb/Views ./src/AccountGoWeb/Components -type f \( -name "*.cshtml" -o -name "*.razor" \))

# Count before
echo "Before migration:"
echo "  'fa fa-' (FA4): $(grep -ro 'fa fa-' $FILES | wc -l | tr -d ' ')"
echo "  'fas fa-' (FA5+): $(grep -ro 'fas fa-' $FILES | wc -l | tr -d ' ')"
echo "  'far fa-' (FA5+): $(grep -ro 'far fa-' $FILES | wc -l | tr -d ' ')"
echo ""

UPDATED=0

# Process each file
for FILE in $FILES; do
    if grep -q 'class="fa fa-' "$FILE" 2>/dev/null; then
        # Replace fa fa- with fas fa- (solid is default in FA6)
        sed -i '' 's/class="fa fa-/class="fas fa-/g' "$FILE"
        sed -i '' "s/class='fa fa-/class='fas fa-/g" "$FILE"
        
        # Handle multiple classes in same attribute
        sed -i '' 's/\bfa fa-/fas fa-/g' "$FILE"
        
        UPDATED=$((UPDATED + 1))
        echo "Updated: $FILE"
    fi
done

echo ""
echo "=== Migration Summary ==="
echo "Files updated: $UPDATED"
echo ""
echo "After migration:"
echo "  'fa fa-' (FA4): $(grep -ro 'fa fa-' $FILES | wc -l | tr -d ' ')"
echo "  'fas fa-' (FA6 solid): $(grep -ro 'fas fa-' $FILES | wc -l | tr -d ' ')"
echo ""
echo "✅ Font Awesome 6 migration complete!"
