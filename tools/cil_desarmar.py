"""Desarma un .net.dll (ScriptHookDotNet) a IL legible. Uso: python cil_desarmar.py Mod.net.dll [filtro]
Requiere: pip install dnfile dncil"""
import sys, dnfile
from dncil.cil.body import CilMethodBody
from dncil.cil.body.reader import CilMethodBodyReaderBase
from dncil.clr.token import Token, StringToken, InvalidToken
from dncil.cil.error import MethodBodyFormatError
class R(CilMethodBodyReaderBase):
    def __init__(s,pe,rva): s.pe=pe; s.off=pe.get_offset_from_rva(rva)
    def read(s,n): d=s.pe.get_data(s.pe.get_rva_from_offset(s.off),n); s.off+=n; return d
    def tell(s): return s.off
    def seek(s,o): s.off=o; return o
def tname(pe, tok):
    try:
        t=tok.table; r=tok.rid
        tab={0x01:'TypeRef',0x02:'TypeDef',0x04:'Field',0x06:'MethodDef',0x0A:'MemberRef',0x1B:'TypeSpec',0x2B:'MethodSpec'}.get(t)
        if tab is None: return str(tok)
        row=getattr(pe.net.mdtables,tab).rows[r-1]
        if tab=='MemberRef':
            par=row.Class.row
            pn=getattr(par,'TypeName',None) or getattr(par,'Name','?')
            return f"{pn}::{row.Name}"
        if tab in('TypeRef','TypeDef'): return f"{row.TypeNamespace}.{row.TypeName}"
        if tab=='MethodSpec':
            m=row.Method.row; return f"{getattr(m,'Name','?')}<>"
        return str(getattr(row,'Name',tok))
    except Exception as e: return str(tok)
def main(path, filt=None):
    pe=dnfile.dnPE(path)
    tdefs=pe.net.mdtables.TypeDef.rows
    # map method index to type
    owner={}
    for ti,t in enumerate(tdefs):
        for m in t.MethodList: owner[m.row_index]=t.TypeName
    for i,m in enumerate(pe.net.mdtables.MethodDef.rows):
        if not m.Rva: continue
        name=f"{owner.get(i+1,'?')}::{m.Name}"
        if filt and filt not in name: continue
        try: body=CilMethodBody(R(pe,m.Rva))
        except Exception as e: print('\n== ERR',name,e); continue
        print(f"\n== {name}")
        for ins in body.instructions:
            op=ins.operand
            if isinstance(op,StringToken):
                try: s=str(pe.net.user_strings.get(op.rid)); ops=repr(s)
                except Exception: ops=str(op)
            elif isinstance(op,Token): ops=tname(pe,op)
            else: ops='' if op is None else str(op)
            print(f"  {ins.opcode} {ops}")
main(sys.argv[1], sys.argv[2] if len(sys.argv)>2 else None)
