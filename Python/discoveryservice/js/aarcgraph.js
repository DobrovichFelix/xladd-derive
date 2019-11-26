
var network;
var container;
var exportArea;
var importButton;
var exportButton;

function init() {
    container = document.getElementById('mynetwork');
    exportArea = document.getElementById('input_output');
    importButton = document.getElementById('import_button');
    exportButton = document.getElementById('export_button');

    meshNetwork();
}

function meshNetwork() {
    var meshTest = 'digraph ROUTES {    "127.0.0.1:6002"->"127.0.0.1:6001" [label = "q:newcloseprice" ];    "127.0.0.1:6003"->"127.0.0.1:6001" [label = "q:newrandom" ];}';

    var inputData = meshRoutes();

    var data = {
        nodes: getNodeData(inputData),
        edges: getEdgeData(inputData)
    }
    var options = {
        layout: {
            //            randomSeed: undefined,
            //            improvedLayout:true,
            //            clusterThreshold: 150,
            hierarchical: {
                enabled: true,
                levelSeparation: 150,
                nodeSpacing: 100,
                treeSpacing: 200,
                blockShifting: true,
                edgeMinimization: true,
                parentCentralization: true,
                direction: 'LR',        // UD, DU, LR, RL
                sortMethod: 'directed',  // hubsize, directed
                //              shakeTowards: 'roots'  // roots, leaves
            }
        },
        physics: {
            enabled: false
        },
        edges: {
            arrows: "to"
        },
        configure: {
            filter: function (option, path) {
                if (path.indexOf('hierarchical') !== -1) {
                    return true;
                }
                return false;
            },
            showButton: false
        },
        interaction: {
            dragNodes: true,
            dragView: true,
            hideEdgesOnDrag: false,
//            hideEdgesOnZoom: false,
            hideNodesOnDrag: false,
            hover: false,
            hoverConnectedEdges: true,
            keyboard: {
                enabled: false,
                speed: { x: 10, y: 10, zoom: 0.02 },
                bindToWindow: true
            },
            multiselect: false,
            navigationButtons: false,
            selectable: true,
            selectConnectedEdges: true,
            tooltipDelay: 300,
            zoomView: true
        },
        groups:
        {
            process:
            {
                shape: 'box',
                color: "#00FFFF" // yellow
            },
            queue:
            {
                shape: 'database',
                color: "#FFFF00" // yellow
            },
            database:
            {
                shape: 'circle',
                color: "#FFFF00" // yellow
            },
            server:
            {
                shape: 'box',
                color: {
                    border: '#2B7CE9',
                    background: '#97C2FC',
                    highlight: {
                        border: '#2B7CE9',
                        background: '#D2E5FF'
                    },
                    hover: {
                        border: '#2B7CE9',
                        background: '#D2E5FF'
                    }
                }
            }
        }
    };
    network = new vis.Network(container, data, options);

    resizeExportArea();
}

function addConnections(elem, index) {
    // need to replace this with a tree of the network, then get child direct children of the element
    elem.connections = network.getConnectedNodes(index);
}

function destroyNetwork() {
    network.destroy();
}

function clearOutputArea() {
    exportArea.value = "";
}

function draw() {
    // create a network of nodes
    var data = getScaleFreeNetwork(5);

    network = new vis.Network(container, data, { manipulation: { enabled: true } });

    clearOutputArea();
}

function exportNetwork() {
    clearOutputArea();

    var nodes = objectToArray(network.getPositions());

    nodes.forEach(addConnections);

    // pretty print node data
    var exportValue = JSON.stringify(nodes, undefined, 2);

    exportArea.value = exportValue;

    resizeExportArea();
}

function importNetwork() {
    var inputValue = exportArea.value;
    var inputData = JSON.parse(inputValue);

    var data = {
        nodes: getNodeData(inputData),
        edges: getEdgeData(inputData)
    }

    network = new vis.Network(container, data, {});

    resizeExportArea();
}

function getNodeData(data) {
    var networkNodes = [];

    data.forEach(function (elem, index, array) {
        networkNodes.push({ id: elem.id, label: elem.label, group: elem.group, x: elem.x, y: elem.y });
    });

    return new vis.DataSet(networkNodes);
}

function getNodeById(data, id) {
    for (var n = 0; n < data.length; n++) {
        if (data[n].id == id) {  // double equals since id can be numeric or string
            return data[n];
        }
    };

    throw 'Can not find id \'' + id + '\' in data';
}

function getEdgeData(data) {
    var networkEdges = [];

    data.forEach(function (node) {
        // add the connection
        node.connections.forEach(function (connId, cIndex, conns) {
            networkEdges.push({ from: node.id, to: connId });
            let cNode = getNodeById(data, connId);

            var elementConnections = cNode.connections;

            // remove the connection from the other node to prevent duplicate connections
            var duplicateIndex = elementConnections.findIndex(function (connection) {
                return connection == node.id; // double equals since id can be numeric or string
            });


            if (duplicateIndex != -1) {
                elementConnections.splice(duplicateIndex, 1);
            };
        });
    });

    return new vis.DataSet(networkEdges);
}

function objectToArray(obj) {
    return Object.keys(obj).map(function (key) {
        obj[key].id = key;
        return obj[key];
    });
}

function resizeExportArea() {
    exportArea.style.height = (1 + exportArea.scrollHeight) + "px";
}

init();

