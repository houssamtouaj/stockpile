using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stockpile.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrdersAndTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    expected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_orders_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_orders_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_orders_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_orders_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    from_warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    in_transit_warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfers", x => x.id);
                    table.CheckConstraint("transfer_three_distinct_warehouses", "from_warehouse_id <> to_warehouse_id AND in_transit_warehouse_id <> from_warehouse_id AND in_transit_warehouse_id <> to_warehouse_id");
                    table.ForeignKey(
                        name: "fk_stock_transfers_warehouses_from_warehouse_id",
                        column: x => x.from_warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfers_warehouses_in_transit_warehouse_id",
                        column: x => x.in_transit_warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfers_warehouses_to_warehouse_id",
                        column: x => x.to_warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_ordered = table.Column<int>(type: "integer", nullable: false),
                    quantity_received = table.Column<int>(type: "integer", nullable: false),
                    unit_cost_cents = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_lines", x => x.id);
                    table.CheckConstraint("po_line_cost_not_negative", "unit_cost_cents >= 0");
                    table.CheckConstraint("po_line_quantity_positive", "quantity_ordered > 0");
                    table.CheckConstraint("po_line_received_within_ordered", "quantity_received >= 0 AND quantity_received <= quantity_ordered");
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_order_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_ordered = table.Column<int>(type: "integer", nullable: false),
                    quantity_picked = table.Column<int>(type: "integer", nullable: false),
                    unit_price_cents = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_order_lines", x => x.id);
                    table.CheckConstraint("so_line_picked_within_ordered", "quantity_picked >= 0 AND quantity_picked <= quantity_ordered");
                    table.CheckConstraint("so_line_quantity_positive", "quantity_ordered > 0");
                    table.ForeignKey(
                        name: "fk_sales_order_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_order_lines_sales_orders_sales_order_id",
                        column: x => x.sales_order_id,
                        principalTable: "sales_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer_lines", x => x.id);
                    table.CheckConstraint("transfer_line_quantity_positive", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_stock_transfer_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_lines_stock_transfers_stock_transfer_id",
                        column: x => x.stock_transfer_id,
                        principalTable: "stock_transfers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_product_id",
                table: "purchase_order_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_purchase_order_id_product_id",
                table: "purchase_order_lines",
                columns: new[] { "purchase_order_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_number",
                table: "purchase_orders",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_supplier_id",
                table: "purchase_orders",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_warehouse_id_status",
                table: "purchase_orders",
                columns: new[] { "warehouse_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_lines_product_id",
                table: "sales_order_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_lines_sales_order_id_product_id",
                table: "sales_order_lines",
                columns: new[] { "sales_order_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_customer_id",
                table: "sales_orders",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_number",
                table: "sales_orders",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_warehouse_id_status",
                table: "sales_orders",
                columns: new[] { "warehouse_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_product_id",
                table: "stock_transfer_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_stock_transfer_id_product_id",
                table: "stock_transfer_lines",
                columns: new[] { "stock_transfer_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_from_warehouse_id_status",
                table: "stock_transfers",
                columns: new[] { "from_warehouse_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_in_transit_warehouse_id",
                table: "stock_transfers",
                column: "in_transit_warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_number",
                table: "stock_transfers",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_to_warehouse_id_status",
                table: "stock_transfers",
                columns: new[] { "to_warehouse_id", "status" });

            // Document numbers (PO-2026-00001). Sequences rather than MAX()+1, which races
            // under concurrent creation; see DocumentNumberGenerator.
            migrationBuilder.Sql("""
                CREATE SEQUENCE IF NOT EXISTS purchase_order_number_seq START 1;
                CREATE SEQUENCE IF NOT EXISTS sales_order_number_seq    START 1;
                CREATE SEQUENCE IF NOT EXISTS stock_transfer_number_seq START 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP SEQUENCE IF EXISTS purchase_order_number_seq;
                DROP SEQUENCE IF EXISTS sales_order_number_seq;
                DROP SEQUENCE IF EXISTS stock_transfer_number_seq;
                """);

            migrationBuilder.DropTable(
                name: "purchase_order_lines");

            migrationBuilder.DropTable(
                name: "sales_order_lines");

            migrationBuilder.DropTable(
                name: "stock_transfer_lines");

            migrationBuilder.DropTable(
                name: "purchase_orders");

            migrationBuilder.DropTable(
                name: "sales_orders");

            migrationBuilder.DropTable(
                name: "stock_transfers");
        }
    }
}
