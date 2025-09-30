# Y-Position Logic Documentation for PDF Sheet Builder

## Overview
This document details the exact Y-position (vertical positioning) logic used in the ConsoleApp1_vdp_sheetbuilder application for placing PDF pages on custom sheets. This information is essential for implementing the same Y-position logic in a web version of the PDF sheet builder.

## Core Constants and Dimensions

### Sheet Dimensions
```csharp
// Lines 600-601 & 832-833 in Program.cs
float widthInPoints = 317 / 25.4f * 72;      // Sheet width: 317mm → ~896 points
float maxHeightInPoints = 980 / 25.4f * 72;  // Max sheet height: 980mm → ~2778 points
```

### Floating-Point Tolerance
```csharp
// Line 20 in Program.cs
private const double EPSILON = 0.01;  // ~0.0035mm tolerance for precision errors
```

## Height Calculation Algorithm

### Phase 1: Calculate Total Height (Lines 839-863)
```csharp
float totalHeight = 0;
int pagesOnCustomSheet = 0;

for (int j = pageIndex; j < totalPages; j++)
{
    float sourcePageHeight = sourceDocument.GetPage(pageOrder[j]).GetPageSize().GetHeight();
    
    // EPSILON tolerance check for floating-point precision
    if (totalHeight + sourcePageHeight > maxHeightInPoints + EPSILON)
    {
        break; // Stop adding pages - would exceed sheet height
    }
    
    totalHeight += sourcePageHeight;  // Accumulate total height
    pagesOnCustomSheet++;            // Count pages that fit
}
```

## Y-Position Coordinate System

### PDF Coordinate System Fundamentals
- **Origin**: Bottom-left corner of the sheet (0,0)
- **Y-axis**: Increases upward (mathematical convention)
- **Positioning Strategy**: Start from top and work downward
- **Page Reference Point**: `currentY` represents the BOTTOM edge of each page

### Sheet Creation with Calculated Height
```csharp
// Lines 866-868
PdfPage customPage = outputDocument.AddNewPage(new PageSize(widthInPoints, totalHeight));
PdfCanvas canvas = new PdfCanvas(customPage);
float currentY = totalHeight;  // Start at TOP of the sheet
```

## Core Page Placement Logic

### Main Y-Position Algorithm (Lines 870-883)
```csharp
for (int j = 0; j < pagesOnCustomSheet && pageIndex < totalPages; j++, pageIndex++)
{
    PdfPage sourcePage = sourceDocument.GetPage(pageOrder[pageIndex]);
    float sourcePageHeight = sourcePage.GetPageSize().GetHeight();
    float sourcePageWidth = sourcePage.GetPageSize().GetWidth();
    
    // Calculate horizontal centering
    float xOffset = (widthInPoints - sourcePageWidth) / 2;
    
    // CRITICAL Y-POSITION CALCULATION
    currentY -= sourcePageHeight;  // Move Y position UP by page height
    
    // Place page at calculated position
    canvas.AddXObjectAt(pageCopy, xOffset, currentY);
}
```

### Batch Processing Version (Lines 713-715)
```csharp
// Same logic in batch processing method
float xOffset = (widthInPoints - sourcePageWidth) / 2;
currentY -= sourcePageHeight;  // Identical Y-position logic
```

## Y-Position Flow Example

### For 35 pages of 28mm (79.37 points) each:
```
Sheet Height: 980mm = 2777.953 points
Page Height: 28mm = 79.37 points

Initial State:
currentY = 2777.953 (total height - starting at top)

Page Placement Sequence:
Page 1: currentY = 2777.953 - 79.37 = 2698.583  (top page)
Page 2: currentY = 2698.583 - 79.37 = 2619.213  (second page)
Page 3: currentY = 2619.213 - 79.37 = 2539.843  (third page)
...
Page 34: currentY = 158.74 - 79.37 = 79.37      (second to last)
Page 35: currentY = 79.37 - 79.37 = 0.000       (bottom page)

Final Result: Perfect fit with 0.000 margin
```

## Rotation Handling

### Y-Position with Rotation (Lines 889-903)
```csharp
if (rotationAngle != 0)
{
    canvas.SaveState();
    
    // Calculate center point for rotation
    float centerX = xOffset + (sourcePageWidth / 2);
    float centerY = currentY + (sourcePageHeight / 2);  // Center of current page
    
    // Apply rotation transformations around center point
    canvas.ConcatMatrix(1, 0, 0, 1, centerX, centerY);
    canvas.ConcatMatrix(
        Math.Cos(Math.PI * rotationAngle / 180),
        Math.Sin(Math.PI * rotationAngle / 180),
        -Math.Sin(Math.PI * rotationAngle / 180),
        Math.Cos(Math.PI * rotationAngle / 180),
        0, 0);
    canvas.ConcatMatrix(1, 0, 0, 1, -centerX, -centerY);
    
    canvas.AddXObjectAt(pageCopy, xOffset, currentY);
    canvas.RestoreState();
}
else
{
    // No rotation - direct placement
    canvas.AddXObjectAt(pageCopy, xOffset, currentY);
}
```

## Key Variables for Web Implementation

### Essential Variables
| Variable | Type | Purpose | Initial Value |
|----------|------|---------|---------------|
| `currentY` | float | Current Y position | `totalHeight` |
| `totalHeight` | float | Total calculated height of pages that fit | Calculated sum |
| `sourcePageHeight` | float | Height of individual source page | From PDF page |
| `sourcePageWidth` | float | Width of individual source page | From PDF page |
| `maxHeightInPoints` | float | Maximum allowed sheet height | 2777.953 points |
| `widthInPoints` | float | Sheet width | 896.22 points |
| `xOffset` | float | Horizontal centering offset | `(widthInPoints - sourcePageWidth) / 2` |
| `EPSILON` | double | Floating-point tolerance | 0.01 points |
| `pagesOnCustomSheet` | int | Count of pages that fit on current sheet | Calculated |

### Method References
- **Normal Processing**: Lines 865-912 in `Program.cs`
- **Batch Processing**: Lines 683-760 in `Program.cs`
- **Height Calculation**: Lines 839-863 in `Program.cs`

## Critical Implementation Notes

### 1. Y-Position Calculation Order
```csharp
// ALWAYS subtract height BEFORE placing page
currentY -= sourcePageHeight;
canvas.AddXObjectAt(pageCopy, xOffset, currentY);
```

### 2. Coordinate System Understanding
- `currentY` represents the **bottom edge** of the page being placed
- Pages stack from top to bottom by decreasing `currentY` values
- The first page starts at `totalHeight` and moves down

### 3. Floating-Point Precision
```csharp
// Always use EPSILON tolerance in height comparisons
if (totalHeight + sourcePageHeight > maxHeightInPoints + EPSILON)
{
    break; // Page won't fit
}
```

### 4. Page Centering
```csharp
// Horizontal centering calculation
float xOffset = (widthInPoints - sourcePageWidth) / 2;
```

## Conversion Formulas

### Points to Millimeters
```csharp
double PointsToMm(double points)
{
    return points * 25.4 / 72.0;
}
```

### Millimeters to Points
```csharp
float MmToPoints(float mm)
{
    return mm / 25.4f * 72;
}
```

## Web Implementation Checklist

- [ ] Implement `currentY` tracking variable
- [ ] Start `currentY` at `totalHeight`
- [ ] Subtract `sourcePageHeight` before each page placement
- [ ] Use EPSILON tolerance in height calculations
- [ ] Center pages horizontally with `xOffset`
- [ ] Handle rotation around page center point
- [ ] Maintain bottom-left origin coordinate system
- [ ] Implement floating-point precision handling

## Example Web Implementation Pseudocode

```javascript
// Initialize Y-position tracking
let currentY = totalHeight; // Start at top of sheet

// Place each page
for (let i = 0; i < pagesOnCustomSheet; i++) {
    const sourcePageHeight = getPageHeight(pages[i]);
    const sourcePageWidth = getPageWidth(pages[i]);
    
    // Calculate positions
    const xOffset = (sheetWidth - sourcePageWidth) / 2;
    currentY -= sourcePageHeight; // Move down by page height
    
    // Place page at calculated position
    placePage(pages[i], xOffset, currentY);
}
```

This Y-position logic ensures perfect vertical stacking of pages from top to bottom, with precise height calculations and floating-point error handling.
