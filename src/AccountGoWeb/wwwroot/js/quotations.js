// wwwroot/js/quotations.js
window.initializeGrid = function (quotationsJson, dotNetRef) {
    console.log("initializeGrid called");

    let data = [];
    try {
        data = JSON.parse(quotationsJson);
    } catch (e) {
        console.error("Failed to parse quotations JSON:", e);
    }

    // Column definitions
    const columnDefs = [
        { headerName: "No", field: "no", width: 90, cellClass: "transparentCell" },
        { headerName: "Customer", field: "customerName", flex: 1, cellClass: "transparentCell" },
        { 
            headerName: "Date", 
            field: "quotationDate", 
            width: 180, 
            valueFormatter: p => p.value ? new Date(p.value).toLocaleDateString() : "",
            cellClass: "transparentCell"
        },
        { headerName: "Amount", field: "amount", width: 140, cellClass: "transparentCell" },
        { headerName: "Status", field: "salesQuoteStatus", width: 140, cellClass: "transparentCell" }
    ];

    // Grid options
    const gridOptions = {
        columnDefs: columnDefs,
        rowData: data,
        rowSelection: "single",
        getRowClass: () => 'transparentRow',
        onRowClicked: function (event) {
            console.log("Row clicked:", event.data); // debug
            dotNetRef.invokeMethodAsync("OnSelectionChanged", event.data.id, event.data.statusId);
        },
        defaultColDef: {
            resizable: true,
            sortable: true,
            filter: true
        },
        suppressRowHoverHighlight: true // optional, prevent hover background
    };

    const eGridDiv = document.getElementById("quotationsGrid");
    
    // Destroy previous grid if exists
    if (eGridDiv.__agGridInstance) {
        eGridDiv.__agGridInstance.api.destroy();
    }

    // Create grid
    agGrid.createGrid(eGridDiv, gridOptions);
};
