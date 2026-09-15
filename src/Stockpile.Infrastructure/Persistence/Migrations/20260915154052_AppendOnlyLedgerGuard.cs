using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stockpile.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The spec says StockMovement and AuditEntry are never updated and never deleted.
    /// A comment does not enforce that; a trigger does.
    /// <para>
    /// Scoped to UPDATE and DELETE only. TRUNCATE is deliberately not covered, which is
    /// what lets the integration suite reset the database between tests.
    /// </para>
    /// </summary>
    public partial class AppendOnlyLedgerGuard : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION reject_ledger_mutation() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION
                        'Table % is append-only; % is not permitted.', TG_TABLE_NAME, TG_OP
                        USING ERRCODE = 'restrict_violation';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER stock_movements_append_only
                    BEFORE UPDATE OR DELETE ON stock_movements
                    FOR EACH ROW EXECUTE FUNCTION reject_ledger_mutation();

                CREATE TRIGGER audit_entries_append_only
                    BEFORE UPDATE OR DELETE ON audit_entries
                    FOR EACH ROW EXECUTE FUNCTION reject_ledger_mutation();
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS stock_movements_append_only ON stock_movements;
                DROP TRIGGER IF EXISTS audit_entries_append_only ON audit_entries;
                DROP FUNCTION IF EXISTS reject_ledger_mutation();
                """);
        }
    }
}
