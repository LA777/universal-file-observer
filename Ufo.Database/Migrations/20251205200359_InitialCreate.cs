using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ufo.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Files",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    FileExtension = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: true),
                    Sha256Hash = table.Column<string>(type: "TEXT", nullable: false),
                    FullPath = table.Column<string>(type: "TEXT", nullable: true),
                    HasParent = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsHidden = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Files", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Folders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: true),
                    Sha256Hash = table.Column<string>(type: "TEXT", nullable: false),
                    FullPath = table.Column<string>(type: "TEXT", nullable: true),
                    HasParent = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsHidden = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Folders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Pcs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pcs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StorageDrives",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", nullable: false),
                    SerialNumber = table.Column<string>(type: "TEXT", nullable: false),
                    TotalSize = table.Column<long>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    MediaType = table.Column<string>(type: "TEXT", nullable: false),
                    InterfaceType = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageDrives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FsFileEntityFsFolderEntity",
                columns: table => new
                {
                    FilesId = table.Column<string>(type: "TEXT", nullable: false),
                    ParentFoldersId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FsFileEntityFsFolderEntity", x => new { x.FilesId, x.ParentFoldersId });
                    table.ForeignKey(
                        name: "FK_FsFileEntityFsFolderEntity_Files_FilesId",
                        column: x => x.FilesId,
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FsFileEntityFsFolderEntity_Folders_ParentFoldersId",
                        column: x => x.ParentFoldersId,
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FsFolderEntityFsFolderEntity",
                columns: table => new
                {
                    ChildFoldersId = table.Column<string>(type: "TEXT", nullable: false),
                    ParentFoldersId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FsFolderEntityFsFolderEntity", x => new { x.ChildFoldersId, x.ParentFoldersId });
                    table.ForeignKey(
                        name: "FK_FsFolderEntityFsFolderEntity_Folders_ChildFoldersId",
                        column: x => x.ChildFoldersId,
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FsFolderEntityFsFolderEntity_Folders_ParentFoldersId",
                        column: x => x.ParentFoldersId,
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcEntityStorageDriveEntity",
                columns: table => new
                {
                    PcsId = table.Column<string>(type: "TEXT", nullable: false),
                    StorageDrivesId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcEntityStorageDriveEntity", x => new { x.PcsId, x.StorageDrivesId });
                    table.ForeignKey(
                        name: "FK_PcEntityStorageDriveEntity_Pcs_PcsId",
                        column: x => x.PcsId,
                        principalTable: "Pcs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PcEntityStorageDriveEntity_StorageDrives_StorageDrivesId",
                        column: x => x.StorageDrivesId,
                        principalTable: "StorageDrives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Snapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RootFolderId = table.Column<string>(type: "TEXT", nullable: true),
                    FsFileEntityId = table.Column<string>(type: "TEXT", nullable: true),
                    PcEntityId = table.Column<string>(type: "TEXT", nullable: true),
                    StorageDriveEntityId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Snapshots_Files_FsFileEntityId",
                        column: x => x.FsFileEntityId,
                        principalTable: "Files",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Snapshots_Folders_RootFolderId",
                        column: x => x.RootFolderId,
                        principalTable: "Folders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Snapshots_Pcs_PcEntityId",
                        column: x => x.PcEntityId,
                        principalTable: "Pcs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Snapshots_StorageDrives_StorageDriveEntityId",
                        column: x => x.StorageDriveEntityId,
                        principalTable: "StorageDrives",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Volumes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    DriveLetter = table.Column<string>(type: "TEXT", nullable: false),
                    VolumeName = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    VolumeSerialNumber = table.Column<string>(type: "TEXT", nullable: false),
                    VolumeSize = table.Column<long>(type: "INTEGER", nullable: false),
                    StorageDriveId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Volumes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Volumes_StorageDrives_StorageDriveId",
                        column: x => x.StorageDriveId,
                        principalTable: "StorageDrives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FilesToFolders",
                columns: table => new
                {
                    SnapshotId = table.Column<string>(type: "TEXT", nullable: false),
                    FolderId = table.Column<string>(type: "TEXT", nullable: false),
                    FileId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilesToFolders", x => new { x.FolderId, x.FileId, x.SnapshotId });
                    table.ForeignKey(
                        name: "FK_FilesToFolders_Files_FileId",
                        column: x => x.FileId,
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FilesToFolders_Folders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FilesToFolders_Snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "Snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FoldersToFolders",
                columns: table => new
                {
                    SnapshotId = table.Column<string>(type: "TEXT", nullable: false),
                    ParentFolderId = table.Column<string>(type: "TEXT", nullable: false),
                    ChildFolderId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoldersToFolders", x => new { x.ParentFolderId, x.ChildFolderId, x.SnapshotId });
                    table.ForeignKey(
                        name: "FK_FoldersToFolders_Folders_ChildFolderId",
                        column: x => x.ChildFolderId,
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FoldersToFolders_Folders_ParentFolderId",
                        column: x => x.ParentFolderId,
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FoldersToFolders_Snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "Snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcsToStorageDrives",
                columns: table => new
                {
                    SnapshotId = table.Column<string>(type: "TEXT", nullable: false),
                    PcId = table.Column<string>(type: "TEXT", nullable: false),
                    StorageDriveId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcsToStorageDrives", x => new { x.PcId, x.StorageDriveId, x.SnapshotId });
                    table.ForeignKey(
                        name: "FK_PcsToStorageDrives_Pcs_PcId",
                        column: x => x.PcId,
                        principalTable: "Pcs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PcsToStorageDrives_Snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "Snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PcsToStorageDrives_StorageDrives_StorageDriveId",
                        column: x => x.StorageDriveId,
                        principalTable: "StorageDrives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VolumeInfos",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    FreeSpace = table.Column<long>(type: "INTEGER", nullable: false),
                    DriveStatus = table.Column<string>(type: "TEXT", nullable: false),
                    VolumeId = table.Column<string>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VolumeInfos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VolumeInfos_Snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "Snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VolumeInfos_Volumes_VolumeId",
                        column: x => x.VolumeId,
                        principalTable: "Volumes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FilesToFolders_FileId",
                table: "FilesToFolders",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_FilesToFolders_SnapshotId",
                table: "FilesToFolders",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_FoldersToFolders_ChildFolderId",
                table: "FoldersToFolders",
                column: "ChildFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_FoldersToFolders_SnapshotId",
                table: "FoldersToFolders",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_FsFileEntityFsFolderEntity_ParentFoldersId",
                table: "FsFileEntityFsFolderEntity",
                column: "ParentFoldersId");

            migrationBuilder.CreateIndex(
                name: "IX_FsFolderEntityFsFolderEntity_ParentFoldersId",
                table: "FsFolderEntityFsFolderEntity",
                column: "ParentFoldersId");

            migrationBuilder.CreateIndex(
                name: "IX_PcEntityStorageDriveEntity_StorageDrivesId",
                table: "PcEntityStorageDriveEntity",
                column: "StorageDrivesId");

            migrationBuilder.CreateIndex(
                name: "IX_PcsToStorageDrives_SnapshotId",
                table: "PcsToStorageDrives",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_PcsToStorageDrives_StorageDriveId",
                table: "PcsToStorageDrives",
                column: "StorageDriveId");

            migrationBuilder.CreateIndex(
                name: "IX_Snapshots_FsFileEntityId",
                table: "Snapshots",
                column: "FsFileEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Snapshots_PcEntityId",
                table: "Snapshots",
                column: "PcEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Snapshots_RootFolderId",
                table: "Snapshots",
                column: "RootFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_Snapshots_StorageDriveEntityId",
                table: "Snapshots",
                column: "StorageDriveEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_VolumeInfos_SnapshotId",
                table: "VolumeInfos",
                column: "SnapshotId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VolumeInfos_VolumeId",
                table: "VolumeInfos",
                column: "VolumeId");

            migrationBuilder.CreateIndex(
                name: "IX_Volumes_StorageDriveId",
                table: "Volumes",
                column: "StorageDriveId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FilesToFolders");

            migrationBuilder.DropTable(
                name: "FoldersToFolders");

            migrationBuilder.DropTable(
                name: "FsFileEntityFsFolderEntity");

            migrationBuilder.DropTable(
                name: "FsFolderEntityFsFolderEntity");

            migrationBuilder.DropTable(
                name: "PcEntityStorageDriveEntity");

            migrationBuilder.DropTable(
                name: "PcsToStorageDrives");

            migrationBuilder.DropTable(
                name: "VolumeInfos");

            migrationBuilder.DropTable(
                name: "Snapshots");

            migrationBuilder.DropTable(
                name: "Volumes");

            migrationBuilder.DropTable(
                name: "Files");

            migrationBuilder.DropTable(
                name: "Folders");

            migrationBuilder.DropTable(
                name: "Pcs");

            migrationBuilder.DropTable(
                name: "StorageDrives");
        }
    }
}
