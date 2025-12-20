import { Component, OnInit, inject, signal, Injectable, ChangeDetectionStrategy, ViewChild } from '@angular/core';
//import { NestedTreeControl } from '@angular/cdk/tree';
import { take } from 'rxjs/operators'; // For unsubscribing
import { SnapshotService } from '../../services/snapshot.service';
import { NgModule } from '@angular/core';
 // Important!
import { MatTreeModule, MatTreeNestedDataSource } from '@angular/material/tree';
import { MatIconModule } from '@angular/material/icon';
import { Folder, File, FsItem, Snapshot } from '../../models/models';
import { HttpClient } from '@angular/common/http';
import {MatButtonModule} from '@angular/material/button';
import {ArrayDataSource} from '@angular/cdk/collections';
import {CdkTree, CdkTreeModule} from '@angular/cdk/tree';
import {BehaviorSubject, merge, Observable} from 'rxjs';
import {map} from 'rxjs/operators';
import {MatProgressBarModule} from '@angular/material/progress-bar';


interface TreeNode {
  item: FsItem;
  children?: TreeNode[];
}

interface FoodNode {
  name: string;
  children?: FoodNode[];
}

const TREE_DATA: FoodNode[] = [
  {
    name: 'Fruit',
    children: [{name: 'Apple'}, {name: 'Banana'}, {name: 'Fruit loops'}],
  },
  {
    name: 'Vegetables',
    children: [
      {
        name: 'Green',
        children: [{name: 'Broccoli'}, {name: 'Brussels sprouts'}],
      },
      {
        name: 'Orange',
        children: [{name: 'Pumpkins'}, {name: 'Carrots'}],
      },
    ],
  },
];

export class DynamicFlatNode {
  constructor(
    public item: string,
    public level = 1,
    public expandable = false,
    public isLoading = signal(false),
  ) {}
}

/**
 * Database for dynamic data. When expanding a node in the tree, the data source will need to fetch
 * the descendants data from the database.
 */
@Injectable({providedIn: 'root'})
export class DynamicDatabase {
  dataMap = new Map<string, string[]>([
    ['Fruits', ['Apple', 'Orange', 'Banana']],
    ['Vegetables', ['Tomato', 'Potato', 'Onion']],
    ['Apple', ['Fuji', 'Macintosh']],
    ['Onion', ['Yellow', 'White', 'Purple']],
  ]);

  rootLevelNodes: string[] = ['Fruits', 'Vegetables'];

  /** Initial data from database */
  initialData(): DynamicFlatNode[] {
    return this.rootLevelNodes.map(name => new DynamicFlatNode(name, 0, true));
  }

  getChildren(node: string): string[] | undefined {
    return this.dataMap.get(node);
  }

  isExpandable(node: string): boolean {
    return this.dataMap.has(node);
  }
}

@Component({
  selector: 'app-folder-tree',
  templateUrl: './folder-tree.component.html',
  styleUrls: ['./folder-tree.component.css'],
  standalone: false,
  // imports: [ MatTreeModule, MatButtonModule, MatIconModule, MatProgressBarModule  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FolderTreeComponent implements OnInit {

  @ViewChild(CdkTree)  tree!: CdkTree<FoodNode>;

  private http = inject(HttpClient);
  public snapshot?: Snapshot;
  selectedFolder: Folder | null = null;
  public displayedFiles: File[] = [];
  loading = true; // Add a loading indicator
  childrenAccessor = (node: FoodNode) => node.children ?? [];
  dataSource = new ArrayDataSource(TREE_DATA);
  getLevel = (node: DynamicFlatNode) => node.level;
  isExpandable = (node: DynamicFlatNode) => node.expandable;

  hasChild = (_: number, node: FoodNode) => !!node.children && node.children.length > 0;

  constructor() {
    //const database = inject(DynamicDatabase);

    //this.treeControl = new FlatTreeControl<DynamicFlatNode>(this.getLevel, this.isExpandable);
    //this.dataSource = new ArrayDataSource(database);

    //this.dataSource.data = database.initialData();
  }

  ngOnInit(): void {
    //this.loadFileSystem();

    //this.getLatestSnapshot(); // with backend

    // this.SnapshotService.snapshot$.subscribe((snapshot) => {
    //   this.snapshot = undefined;
    //   this.getSnapshotByGuid(snapshot);
    //   console.log("subscribe - FolderTreeComponent " + snapshot.guid);
    // });
    // const FAMILY_TREE: Family[] = [
    //   {
    //     name: "Joyce",
    //     children: [
    //       { name: "Mike" },
    //       { name: "Will" },
    //       { name: "Eleven", children: [{ name: "Hopper" }] },
    //       { name: "Lucas" },
    //       { name: "Dustin", children: [{ name: "Winona" }] },
    //     ],
    //   },
    //   {
    //     name: "Jean",
    //     children: [{ name: "Otis" }, { name: "Maeve" }],
    //   },
    // ];

    //this.dataSource.data =  TREE_DATA
  }

  // loadFileSystem() {
  //   this.loading = true; // Show loading indicator
  //   this.fileSystemService.getFileSystemData().pipe(take(1)).subscribe({ // Use your service
  //     next: (snapshotData: SnapshotData) => {
  //       const treeData = this.buildTreeData(snapshotData.snapshotEntity.rootFolder);
  //       this.dataSource.data = treeData;
  //       this.selectFolder(snapshotData.snapshotEntity.rootFolder); // Select the root folder initially
  //       this.loading = false; // Hide loading indicator
  //     },
  //     error: (error) => {
  //       console.error("Error loading file system:", error);
  //       this.loading = false; // Hide loading indicator even on error
  //       // Handle the error appropriately, e.g., display an error message to the user.
  //     }
  //   });
  // }

  getLatestSnapshot() {
      this.http.get<Snapshot>('/api/snapshot/latest').subscribe(
        (result) => {
          this.snapshot = result;
          const treeData = this.buildTreeData(result.rootFolder);
          //this.dataSource.data = treeData;
          console.log(result);
        },
        (error) => {
          console.error(error);
        }
      );
    }

    getSnapshotById(snapshot: Snapshot) {
      this.http.get<Snapshot>('/api/snapshot/' + snapshot.id).subscribe(
        (result) => {
          this.snapshot = result;
          const treeData = this.buildTreeData(result.rootFolder);
          //this.dataSource.data = treeData;
          console.log(result);
        },
        (error) => {
          console.error(error);
        }
      );
    }


  buildTreeData(folder: Folder): TreeNode[] {
    const node: TreeNode = { item: folder, children: [] };
    if (folder.childFolders && folder.childFolders.length > 0) {
      node.children = folder.childFolders.map(child => this.buildTreeData(child))[0];
    }
    return [node]; // Return as an array to handle the root node
  }

  //hasChild = (node: TreeNode) => !!node.children && node.children.length > 0;

 // hasChild = (_: number, node: ExampleFlatNode) => node.expandable;

  selectFolder(folder: Folder) {
    this.selectedFolder = folder;
    this.displayedFiles = folder.files;
  }

  getFullPath(item: FsItem): string {  // Make sure you have this or adapt it.
    return item.fullPath; // Or however you access it.
  }

  getParentNode(node: FoodNode) {
    for (const parent of this.flattenNodes(TREE_DATA)) {
      if (parent.children?.includes(node)) {
        return parent;
      }
    }
    return null;
  }

  shouldRender(node: FoodNode): boolean {
    // This node should render if it is a root node or if all of its ancestors are expanded.
    const parent = this.getParentNode(node);
    return !parent || (!!this.tree?.isExpanded(parent) && this.shouldRender(parent));
  }

  flattenNodes(nodes: FoodNode[]): FoodNode[] {
    const flattenedNodes = [];
    for (const node of nodes) {
      flattenedNodes.push(node);
      if (node.children) {
        flattenedNodes.push(...this.flattenNodes(node.children));
      }
    }
    return flattenedNodes;
  }

}
