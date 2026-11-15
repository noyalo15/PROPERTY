import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus } from "lucide-react";
import { useState } from "react";
import { Link } from "react-router-dom";
import { get, post, put } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { label } from "@/lib/format";
import { Button, Card, ErrorText, Field, Input, Modal, OccupancyBar, PageHeader, Select, Spinner, Table, Tabs, Num, NumHead, Pill } from "@/components/ui";

export interface PropertyRow {
  id: string; code: string; name: string; type: string; location?: string; county?: string; defaultCurrency: string;
  landlordId: string; landlord: string; units: number; occupied: number; vacant: number; occupancyPercent: number;
}
export interface Landlord {
  id: string; name: string; type: string; kraPin?: string; phone?: string; email?: string; bankDetails?: string; commissionPercent: number; bcVendorNo?: string;
}

export function PropertiesPage() {
  const [tab, setTab] = useState<"properties" | "landlords">("properties");
  const [q, setQ] = useState("");
  const [editing, setEditing] = useState<PropertyEditable | null>(null);
  const { can } = useAuth();
  const { data, isLoading, error } = useQuery({ queryKey: ["properties", q], queryFn: () => get<PropertyRow[]>(`/api/properties?q=${encodeURIComponent(q)}`) });

  return (
    <div>
      <PageHeader title="Properties & Units" actions={can("property_manager") && tab === "properties" && <Button onClick={() => setEditing({ type: "residential", defaultCurrency: "KES" })}><Plus className="h-4 w-4" />New property</Button>}>
        Property codes and unit codes are the PROPERTY and UNIT dimension values in Business Central.
      </PageHeader>
      <Tabs tabs={[{ id: "properties", label: "Properties", count: data?.length }, { id: "landlords", label: "Landlords" }]} value={tab} onChange={setTab} />
      <div className="mt-4">
        {tab === "landlords" ? <LandlordsTab /> : (
          <Card padded={false} title={<Input placeholder="Filter by name, code or location" value={q} onChange={(e) => setQ(e.target.value)} className="w-72" />}>
            {isLoading ? <Spinner /> : error ? <div className="p-4"><ErrorText error={error} /></div> : (
              <Table head={["Property", "Type", "Landlord", "Currency", <NumHead key="u">Units</NumHead>, <NumHead key="v">Vacant</NumHead>, "Occupancy"]}>
                {data!.map((p) => (
                  <tr key={p.id}>
                    <td><Link to={`/properties/${p.id}`} className="font-medium text-slate-800 hover:text-teal">{p.name}</Link><div className="text-xs text-slate-400">{p.code} · {p.location}</div></td>
                    <td><Pill tone={p.type === "commercial" ? "blue" : p.type === "mixed" ? "amber" : "teal"}>{label(p.type)}</Pill></td>
                    <td className="text-slate-600">{p.landlord}</td>
                    <td>{p.defaultCurrency}</td>
                    <Num>{p.units}</Num><Num>{p.vacant}</Num>
                    <td><OccupancyBar percent={p.occupancyPercent} /></td>
                  </tr>
                ))}
              </Table>
            )}
          </Card>
        )}
      </div>
      {editing && <PropertyForm value={editing} onClose={() => setEditing(null)} />}
    </div>
  );
}

type PropertyEditable = Partial<Pick<PropertyRow, "id" | "landlordId" | "code" | "name" | "type" | "location" | "county" | "defaultCurrency">> & { bulkWaterMeterNo?: string };

export function PropertyForm({ value, onClose }: { value: PropertyEditable; onClose: () => void }) {
  const qc = useQueryClient();
  const [f, setF] = useState({ landlordId: value.landlordId ?? "", code: value.code ?? "", name: value.name ?? "", type: value.type ?? "residential",
    location: value.location ?? "", county: value.county ?? "", bulkWaterMeterNo: value.bulkWaterMeterNo ?? "", defaultCurrency: value.defaultCurrency ?? "KES" });
  const landlords = useQuery({ queryKey: ["landlords"], queryFn: () => get<Landlord[]>("/api/landlords") });
  const save = useMutation({
    mutationFn: () => (value.id ? put(`/api/properties/${value.id}`, f) : post("/api/properties", f)),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["properties"] }); qc.invalidateQueries({ queryKey: ["property"] }); onClose(); },
  });
  const set = (k: keyof typeof f) => (e: { target: { value: string } }) => setF({ ...f, [k]: e.target.value });
  return (
    <Modal open title={value.id ? "Edit property" : "New property"} onClose={onClose}
      footer={<><Button variant="secondary" onClick={onClose}>Cancel</Button><Button busy={save.isPending} onClick={() => save.mutate()}>Save</Button></>}>
      <div className="grid gap-3 sm:grid-cols-2">
        <ErrorText error={save.error} />
        <Field label="Landlord" className="sm:col-span-2"><Select value={f.landlordId} onChange={set("landlordId")} placeholder="Choose…" options={(landlords.data ?? []).map((l) => ({ value: l.id, label: l.name }))} /></Field>
        <Field label="Code" hint="BC PROPERTY dimension value"><Input value={f.code} maxLength={20} onChange={set("code")} /></Field>
        <Field label="Name"><Input value={f.name} onChange={set("name")} /></Field>
        <Field label="Type"><Select value={f.type} onChange={set("type")} options={["residential", "commercial", "mixed"]} /></Field>
        <Field label="Default currency"><Select value={f.defaultCurrency} onChange={set("defaultCurrency")} options={["KES", "USD"]} /></Field>
        <Field label="Location"><Input value={f.location} onChange={set("location")} /></Field>
        <Field label="County"><Input value={f.county} onChange={set("county")} /></Field>
        <Field label="Bulk water meter no." className="sm:col-span-2"><Input value={f.bulkWaterMeterNo} onChange={set("bulkWaterMeterNo")} /></Field>
      </div>
    </Modal>
  );
}

function LandlordsTab() {
  const { can } = useAuth();
  const qc = useQueryClient();
  const { data, isLoading } = useQuery({ queryKey: ["landlords"], queryFn: () => get<Landlord[]>("/api/landlords") });
  const [edit, setEdit] = useState<Partial<Landlord> | null>(null);
  const save = useMutation({
    mutationFn: (l: Partial<Landlord>) => (l.id ? put(`/api/landlords/${l.id}`, l) : post("/api/landlords", l)),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["landlords"] }); setEdit(null); },
  });
  return (
    <Card padded={false} actions={can("property_manager") && <Button onClick={() => setEdit({ type: "company", commissionPercent: 10 })}><Plus className="h-4 w-4" />New landlord</Button>} title="Landlords">
      {isLoading ? <Spinner /> : (
        <Table head={["Name", "Type", "KRA PIN", "Phone", "Email", <NumHead key="c">Commission</NumHead>, ""]}>
          {data!.map((l) => (
            <tr key={l.id}>
              <td className="font-medium">{l.name}</td><td>{label(l.type)}</td><td>{l.kraPin ?? "—"}</td><td>{l.phone ?? "—"}</td><td>{l.email ?? "—"}</td>
              <Num>{l.commissionPercent}%</Num>
              <td className="text-right">{can("property_manager") && <Button variant="ghost" onClick={() => setEdit(l)}>Edit</Button>}</td>
            </tr>
          ))}
        </Table>
      )}
      {edit && (
        <Modal open title={edit.id ? "Edit landlord" : "New landlord"} onClose={() => setEdit(null)}
          footer={<><Button variant="secondary" onClick={() => setEdit(null)}>Cancel</Button><Button busy={save.isPending} onClick={() => save.mutate(edit)}>Save</Button></>}>
          <div className="grid gap-3 sm:grid-cols-2">
            <ErrorText error={save.error} />
            <Field label="Name" className="sm:col-span-2"><Input value={edit.name ?? ""} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></Field>
            <Field label="Type"><Select value={edit.type} onChange={(e) => setEdit({ ...edit, type: e.target.value })} options={["individual", "company"]} /></Field>
            <Field label="KRA PIN"><Input value={edit.kraPin ?? ""} onChange={(e) => setEdit({ ...edit, kraPin: e.target.value })} /></Field>
            <Field label="Phone"><Input value={edit.phone ?? ""} onChange={(e) => setEdit({ ...edit, phone: e.target.value })} /></Field>
            <Field label="Email"><Input value={edit.email ?? ""} onChange={(e) => setEdit({ ...edit, email: e.target.value })} /></Field>
            <Field label="Commission %"><Input type="number" step="0.01" value={edit.commissionPercent ?? 0} onChange={(e) => setEdit({ ...edit, commissionPercent: Number(e.target.value) })} /></Field>
            <Field label="BC vendor no."><Input value={edit.bcVendorNo ?? ""} onChange={(e) => setEdit({ ...edit, bcVendorNo: e.target.value })} /></Field>
            <Field label="Bank details" className="sm:col-span-2"><Input value={edit.bankDetails ?? ""} onChange={(e) => setEdit({ ...edit, bankDetails: e.target.value })} /></Field>
          </div>
        </Modal>
      )}
    </Card>
  );
}
