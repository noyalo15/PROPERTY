import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import clsx from "clsx";
import { Pencil, Plus, Trash2, Upload } from "lucide-react";
import { useState } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { del, get, post, put, upload } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { date, label, money } from "@/lib/format";
import { Button, Card, ErrorText, Field, Input, Kpi, Modal, PageHeader, Pill, Select, Spinner, StatusPill, Table, Num, NumHead } from "@/components/ui";
import { PropertyForm } from "./Properties";

const UNIT_TYPES = ["bedsitter", "1br", "2br", "3br", "4br", "maisonette", "shop", "office", "warehouse", "parking", "kiosk"];
const STATUS_COLORS: Record<string, string> = {
  occupied: "bg-emerald-100 text-emerald-800 border-emerald-200",
  vacant: "bg-sky-50 text-sky-800 border-sky-200",
  reserved: "bg-amber-50 text-amber-800 border-amber-200",
  maintenance: "bg-orange-50 text-orange-800 border-orange-200",
  off_market: "bg-slate-100 text-slate-500 border-slate-200",
};

interface UnitRow { id: string; code: string; unitType: string; floor?: string; areaSqft?: number; status: string; defaultRent: number; defaultCurrency: string; blockId?: string; block?: string; meters: number; charges: number; tenant?: string }
interface PropertyDetail {
  id: string; code: string; name: string; type: string; location?: string; county?: string; bulkWaterMeterNo?: string; defaultCurrency: string;
  landlordId: string; landlord: string; blocks: { id: string; name: string }[]; units: UnitRow[]; statusCounts: Record<string, number>;
}

export function PropertyDetailPage() {
  const { id } = useParams();
  const [params, setParams] = useSearchParams();
  const { can } = useAuth();
  const qc = useQueryClient();
  const [edit, setEdit] = useState(false);
  const [newUnit, setNewUnit] = useState(false);
  const [importing, setImporting] = useState(false);
  const [filter, setFilter] = useState<string>("");
  const [block, setBlock] = useState("");
  const [blockName, setBlockName] = useState("");
  const { data: p, isLoading, error } = useQuery({ queryKey: ["property", id], queryFn: () => get<PropertyDetail>(`/api/properties/${id}`) });
  const addBlock = useMutation({
    mutationFn: () => post(`/api/properties/${id}/blocks`, { name: blockName }),
    onSuccess: () => { setBlockName(""); qc.invalidateQueries({ queryKey: ["property", id] }); },
  });
  const removeBlock = useMutation({
    mutationFn: (b: string) => del(`/api/properties/${id}/blocks/${b}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["property", id] }),
  });
  if (isLoading) return <Spinner />;
  if (error || !p) return <ErrorText error={error} />;

  const selected = params.get("unit");
  const units = p.units.filter((u) => (!filter || u.status === filter) && (!block || u.blockId === block));
  const c = (s: string) => p.statusCounts[s] ?? 0;
  const write = can("property_manager");

  return (
    <div className="space-y-4">
      <PageHeader title={p.name} actions={write && <>
        <Button variant="secondary" onClick={() => setEdit(true)}><Pencil className="h-4 w-4" />Edit</Button>
        <Button variant="secondary" onClick={() => setImporting(true)}><Upload className="h-4 w-4" />Import units</Button>
        <Button onClick={() => setNewUnit(true)}><Plus className="h-4 w-4" />New unit</Button>
      </>}>
        <Link to="/properties" className="hover:text-teal">Properties</Link> / {p.code} · {label(p.type)} · {p.location}, {p.county} · Landlord: {p.landlord}
      </PageHeader>

      <div className="grid grid-cols-2 gap-3 md:grid-cols-5">
        <Kpi label="Units" value={p.units.length} />
        <Kpi label="Occupied" value={c("occupied")} />
        <Kpi label="Vacant" value={c("vacant")} />
        <Kpi label="Reserved" value={c("reserved")} />
        <Kpi label="Maintenance / off market" value={c("maintenance") + c("off_market")} tone={c("maintenance") ? "warn" : undefined} />
      </div>

      <div className="grid gap-4 lg:grid-cols-4">
        <Card title="Units" className="lg:col-span-3" actions={<>
          <Select value={block} onChange={(e) => setBlock(e.target.value)} placeholder="All blocks" options={p.blocks.map((b) => ({ value: b.id, label: b.name }))} className="w-36" />
          <Select value={filter} onChange={(e) => setFilter(e.target.value)} placeholder="All statuses" options={["occupied", "vacant", "reserved", "maintenance", "off_market"]} className="w-40" />
        </>}>
          <div className="mb-3 flex flex-wrap gap-3 text-xs text-slate-500">
            {Object.entries(STATUS_COLORS).map(([s, cls]) => <span key={s} className="flex items-center gap-1"><span className={clsx("h-3 w-3 rounded border", cls)} />{label(s)}</span>)}
          </div>
          {units.length === 0 ? <p className="py-6 text-center text-sm text-slate-400">No units match.</p> : (
            <div className="grid grid-cols-[repeat(auto-fill,minmax(104px,1fr))] gap-2">
              {units.map((u) => (
                <button key={u.id} onClick={() => setParams({ unit: u.id })}
                  className={clsx("rounded-lg border p-2 text-left transition hover:shadow", STATUS_COLORS[u.status], selected === u.id && "ring-2 ring-teal")}>
                  <div className="text-sm font-semibold">{u.code}</div>
                  <div className="text-[11px] opacity-80">{label(u.unitType)}</div>
                  <div className="truncate text-[11px] opacity-70">{u.tenant ?? label(u.status)}</div>
                </button>
              ))}
            </div>
          )}
        </Card>
        <div className="space-y-4">
          <Card title="Blocks">
            <ul className="space-y-1 text-sm">
              {p.blocks.length === 0 && <li className="text-slate-400">No blocks.</li>}
              {p.blocks.map((b) => (
                <li key={b.id} className="flex items-center justify-between">
                  <span>{b.name} <span className="text-xs text-slate-400">({p.units.filter((u) => u.blockId === b.id).length})</span></span>
                  {write && <button aria-label={`Delete ${b.name}`} className="text-slate-300 hover:text-red-500" onClick={() => removeBlock.mutate(b.id)}><Trash2 className="h-3.5 w-3.5" /></button>}
                </li>
              ))}
            </ul>
            <ErrorText error={removeBlock.error ?? addBlock.error} />
            {write && (
              <form className="mt-3 flex gap-2" onSubmit={(e) => { e.preventDefault(); addBlock.mutate(); }}>
                <Input placeholder="New block" value={blockName} onChange={(e) => setBlockName(e.target.value)} />
                <Button variant="secondary" disabled={!blockName.trim()}>Add</Button>
              </form>
            )}
          </Card>
          <Card title="Details">
            <dl className="space-y-1.5 text-sm">
              <div className="flex justify-between"><dt className="text-slate-500">BC dimension</dt><dd className="font-mono">{p.code}</dd></div>
              <div className="flex justify-between"><dt className="text-slate-500">Currency</dt><dd>{p.defaultCurrency}</dd></div>
              <div className="flex justify-between"><dt className="text-slate-500">Bulk water meter</dt><dd>{p.bulkWaterMeterNo || "—"}</dd></div>
            </dl>
          </Card>
        </div>
      </div>

      {edit && <PropertyForm value={p} onClose={() => setEdit(false)} />}
      {newUnit && <UnitForm property={p} onClose={() => setNewUnit(false)} />}
      {importing && <ImportUnits propertyId={p.id} onClose={() => setImporting(false)} />}
      {selected && <UnitDrawer unitId={selected} property={p} onClose={() => setParams({})} />}
    </div>
  );
}

type UnitEditable = Pick<UnitRow, "id" | "code" | "unitType" | "floor" | "areaSqft" | "status" | "defaultRent" | "defaultCurrency" | "blockId">;

function UnitForm({ property, unit, onClose }: { property: PropertyDetail; unit?: Partial<UnitEditable>; onClose: () => void }) {
  const qc = useQueryClient();
  const [f, setF] = useState({
    propertyId: property.id, blockId: unit?.blockId ?? "", code: unit?.code ?? "", unitType: unit?.unitType ?? (property.type === "commercial" ? "office" : "1br"),
    floor: unit?.floor ?? "", areaSqft: unit?.areaSqft?.toString() ?? "", status: unit?.status ?? "vacant",
    defaultRent: unit?.defaultRent?.toString() ?? "", defaultCurrency: unit?.defaultCurrency ?? property.defaultCurrency,
  });
  const body = () => ({ ...f, blockId: f.blockId || null, areaSqft: f.areaSqft ? Number(f.areaSqft) : null, defaultRent: Number(f.defaultRent || 0), floor: f.floor || null });
  const save = useMutation({
    mutationFn: () => (unit?.id ? put(`/api/units/${unit.id}`, body()) : post("/api/units", body())),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["property", property.id] }); qc.invalidateQueries({ queryKey: ["unit"] }); onClose(); },
  });
  const set = (k: keyof typeof f) => (e: { target: { value: string } }) => setF({ ...f, [k]: e.target.value });
  const statuses = unit?.status === "occupied" ? ["occupied"] : ["vacant", "reserved", "maintenance", "off_market"];
  return (
    <Modal open title={unit?.id ? `Edit unit ${unit.code}` : "New unit"} onClose={onClose}
      footer={<><Button variant="secondary" onClick={onClose}>Cancel</Button><Button busy={save.isPending} onClick={() => save.mutate()}>Save</Button></>}>
      <div className="grid gap-3 sm:grid-cols-2">
        <ErrorText error={save.error} />
        <Field label="Code" hint="BC UNIT dimension value, e.g. A-104"><Input value={f.code} maxLength={20} onChange={set("code")} /></Field>
        <Field label="Block"><Select value={f.blockId} onChange={set("blockId")} placeholder="None" options={property.blocks.map((b) => ({ value: b.id, label: b.name }))} /></Field>
        <Field label="Type"><Select value={f.unitType} onChange={set("unitType")} options={UNIT_TYPES} /></Field>
        <Field label="Status" hint={unit?.status === "occupied" ? "Set by the active lease" : "Occupied is set when a lease is activated"}><Select value={f.status} onChange={set("status")} options={statuses} disabled={unit?.status === "occupied"} /></Field>
        <Field label="Floor"><Input value={f.floor} onChange={set("floor")} /></Field>
        <Field label="Area (sq ft)"><Input type="number" value={f.areaSqft} onChange={set("areaSqft")} /></Field>
        <Field label="Default rent"><Input type="number" step="0.01" value={f.defaultRent} onChange={set("defaultRent")} /></Field>
        <Field label="Currency"><Select value={f.defaultCurrency} onChange={set("defaultCurrency")} options={["KES", "USD"]} /></Field>
      </div>
    </Modal>
  );
}

interface UnitDetail {
  id: string; code: string; unitType: string; floor?: string; areaSqft?: number; status: string; defaultRent: number; defaultCurrency: string; blockId?: string; block?: string;
  charges: { id: string; chargeCode: string; amount: number; currencyCode: string; frequency: string }[];
  meters: { id: string; meterType: string; serialNo: string; lastReading?: number; lastReadingDate?: string; active: boolean }[];
  activeLease?: { id: string; leaseNo: string; startDate: string; endDate?: string; tenantId: string; tenantNo: string; fullName: string };
}

function UnitDrawer({ unitId, property, onClose }: { unitId: string; property: PropertyDetail; onClose: () => void }) {
  const { can } = useAuth();
  const qc = useQueryClient();
  const { data: u, isLoading } = useQuery({ queryKey: ["unit", unitId], queryFn: () => get<UnitDetail>(`/api/units/${unitId}`) });
  const codes = useQuery({ queryKey: ["charge-codes"], queryFn: () => get<{ code: string; description: string }[]>("/api/setup/charge-codes") });
  const [editing, setEditing] = useState(false);
  const [charges, setCharges] = useState<UnitDetail["charges"] | null>(null);
  const [meter, setMeter] = useState({ meterType: "water", serialNo: "", lastReading: "" });
  const saveCharges = useMutation({
    mutationFn: () => put(`/api/units/${unitId}/charges`, charges!.map((c) => ({ ...c, amount: Number(c.amount) }))),
    onSuccess: () => { setCharges(null); qc.invalidateQueries({ queryKey: ["unit", unitId] }); qc.invalidateQueries({ queryKey: ["property", property.id] }); },
  });
  const addMeter = useMutation({
    mutationFn: () => post(`/api/units/${unitId}/meters`, { ...meter, lastReading: meter.lastReading ? Number(meter.lastReading) : null, active: true }),
    onSuccess: () => { setMeter({ meterType: "water", serialNo: "", lastReading: "" }); qc.invalidateQueries({ queryKey: ["unit", unitId] }); },
  });
  const write = can("property_manager");

  return (
    <Modal open wide title={u ? `Unit ${u.code}` : "Unit"} onClose={onClose}>
      {isLoading || !u ? <Spinner /> : (
        <div className="space-y-5">
          <div className="flex flex-wrap items-center gap-3">
            <StatusPill status={u.status} />
            <span className="text-sm text-slate-600">{label(u.unitType)}{u.block ? ` · ${u.block}` : ""}{u.floor ? ` · Floor ${u.floor}` : ""}{u.areaSqft ? ` · ${u.areaSqft} sq ft` : ""}</span>
            <span className="text-sm font-medium">{money(u.defaultRent, u.defaultCurrency)} / month</span>
            {write && <Button variant="secondary" className="ml-auto" onClick={() => setEditing(true)}><Pencil className="h-4 w-4" />Edit</Button>}
          </div>
          {u.activeLease && (
            <div className="rounded-lg border border-line bg-page p-3 text-sm">
              Let to <Link to={`/tenants/${u.activeLease.tenantId}`} className="font-medium text-teal">{u.activeLease.fullName}</Link> ({u.activeLease.tenantNo}) on lease {u.activeLease.leaseNo}, {date(u.activeLease.startDate)} to {date(u.activeLease.endDate)}.
            </div>
          )}

          <section>
            <div className="mb-2 flex items-center justify-between">
              <h4 className="text-sm font-semibold">Default charges <span className="font-normal text-slate-400">(copied to new leases)</span></h4>
              {write && !charges && <Button variant="ghost" onClick={() => setCharges(u.charges.map((c) => ({ ...c })))}>Edit charges</Button>}
            </div>
            {charges ? (
              <div className="space-y-2">
                {charges.map((c, i) => (
                  <div key={i} className="grid grid-cols-[1fr_110px_80px_110px_32px] gap-2">
                    <Select value={c.chargeCode} onChange={(e) => setCharges(charges.map((x, j) => (j === i ? { ...x, chargeCode: e.target.value } : x)))} options={(codes.data ?? []).map((cc) => ({ value: cc.code, label: `${cc.code} · ${cc.description}` }))} />
                    <Input type="number" step="0.01" value={c.amount} onChange={(e) => setCharges(charges.map((x, j) => (j === i ? { ...x, amount: e.target.value as unknown as number } : x)))} />
                    <Select value={c.currencyCode} onChange={(e) => setCharges(charges.map((x, j) => (j === i ? { ...x, currencyCode: e.target.value } : x)))} options={["KES", "USD"]} />
                    <Select value={c.frequency} onChange={(e) => setCharges(charges.map((x, j) => (j === i ? { ...x, frequency: e.target.value } : x)))} options={["monthly", "quarterly", "semi_annual", "annual", "once"]} />
                    <button aria-label="Remove charge" className="text-slate-400 hover:text-red-500" onClick={() => setCharges(charges.filter((_, j) => j !== i))}><Trash2 className="h-4 w-4" /></button>
                  </div>
                ))}
                <ErrorText error={saveCharges.error} />
                <div className="flex gap-2">
                  <Button variant="secondary" onClick={() => setCharges([...charges, { id: "", chargeCode: "SERVICE", amount: 0, currencyCode: u.defaultCurrency, frequency: "monthly" }])}><Plus className="h-4 w-4" />Add</Button>
                  <Button busy={saveCharges.isPending} onClick={() => saveCharges.mutate()}>Save charges</Button>
                  <Button variant="ghost" onClick={() => setCharges(null)}>Cancel</Button>
                </div>
              </div>
            ) : (
              <Table head={["Charge", "Frequency", <NumHead key="a">Amount</NumHead>]}>
                {u.charges.length === 0 && <tr><td colSpan={3} className="text-slate-400">No default charges.</td></tr>}
                {u.charges.map((c) => <tr key={c.id}><td>{c.chargeCode}</td><td>{label(c.frequency)}</td><Num>{money(c.amount, c.currencyCode)}</Num></tr>)}
              </Table>
            )}
          </section>

          <section>
            <h4 className="mb-2 text-sm font-semibold">Meters</h4>
            <Table head={["Type", "Serial no.", <NumHead key="r">Last reading</NumHead>, "Date", "Status"]}>
              {u.meters.length === 0 && <tr><td colSpan={5} className="text-slate-400">No meters.</td></tr>}
              {u.meters.map((m) => (
                <tr key={m.id}><td>{label(m.meterType)}</td><td className="font-mono text-xs">{m.serialNo}</td><Num>{m.lastReading ?? "—"}</Num><td>{date(m.lastReadingDate)}</td><td>{m.active ? <Pill tone="green">Active</Pill> : <Pill>Inactive</Pill>}</td></tr>
              ))}
            </Table>
            {write && (
              <form className="mt-2 grid grid-cols-[120px_1fr_120px_auto] gap-2" onSubmit={(e) => { e.preventDefault(); addMeter.mutate(); }}>
                <Select value={meter.meterType} onChange={(e) => setMeter({ ...meter, meterType: e.target.value })} options={["water", "electricity"]} />
                <Input placeholder="Serial no." value={meter.serialNo} onChange={(e) => setMeter({ ...meter, serialNo: e.target.value })} />
                <Input placeholder="Opening reading" type="number" value={meter.lastReading} onChange={(e) => setMeter({ ...meter, lastReading: e.target.value })} />
                <Button variant="secondary" disabled={!meter.serialNo.trim()} busy={addMeter.isPending}>Add meter</Button>
              </form>
            )}
            <ErrorText error={addMeter.error} />
          </section>
          {editing && <UnitForm property={property} unit={u} onClose={() => setEditing(false)} />}
        </div>
      )}
    </Modal>
  );
}

function ImportUnits({ propertyId, onClose }: { propertyId: string; onClose: () => void }) {
  const qc = useQueryClient();
  const [file, setFile] = useState<File | null>(null);
  const [result, setResult] = useState<{ created: number; updated: number; errors: string[]; dryRun: boolean } | null>(null);
  const run = useMutation({
    mutationFn: async (dryRun: boolean) => {
      const fd = new FormData(); fd.append("file", file!);
      return upload<{ created: number; updated: number; errors: string[]; dryRun: boolean }>(`/api/units/import/${propertyId}?dryRun=${dryRun}`, fd);
    },
    onSuccess: (r) => { setResult(r); if (!r.dryRun) qc.invalidateQueries({ queryKey: ["property", propertyId] }); },
  });
  const template = "code,block,unit_type,floor,area_sqft,default_rent,currency,status\nA-101,A,1br,1,550,32000,KES,vacant\n";
  return (
    <Modal open title="Import units from Excel or CSV" onClose={onClose}
      footer={<><Button variant="secondary" onClick={onClose}>Close</Button>
        <Button variant="secondary" disabled={!file} busy={run.isPending} onClick={() => run.mutate(true)}>Check file</Button>
        <Button disabled={!file || (result !== null && result.errors.length > 0)} busy={run.isPending} onClick={() => run.mutate(false)}>Import</Button></>}>
      <div className="space-y-3 text-sm">
        <p className="text-slate-600">Columns: <span className="font-mono text-xs">code, block, unit_type, floor, area_sqft, default_rent, currency, status</span>. Existing codes are updated, new codes are added, and nothing is saved if any row has an error.</p>
        <a className="text-teal underline" download="units-template.csv" href={`data:text/csv;charset=utf-8,${encodeURIComponent(template)}`}>Download a template</a>
        <Input type="file" accept=".csv,.xlsx" onChange={(e) => { setFile(e.target.files?.[0] ?? null); setResult(null); }} />
        <ErrorText error={run.error} />
        {result && (result.errors.length > 0 ? (
          <div className="max-h-48 overflow-y-auto rounded-lg border border-red-200 bg-red-50 p-3 text-red-700">
            <div className="mb-1 font-medium">{result.errors.length} problem(s); nothing was saved:</div>
            <ul className="list-disc pl-5">{result.errors.map((e) => <li key={e}>{e}</li>)}</ul>
          </div>
        ) : (
          <div className="rounded-lg border border-emerald-200 bg-emerald-50 p-3 text-emerald-800">
            {result.dryRun ? "File is fine: " : "Imported: "}{result.created} new unit(s), {result.updated} updated.
          </div>
        ))}
      </div>
    </Modal>
  );
}
