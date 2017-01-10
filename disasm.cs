using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Nucleus
{

    public partial class AddressMap
    {
        [Flags]
        public enum DisasmRegion
        {
            DISASM_REGION_UNMAPPED = 0x0000,
            DISASM_REGION_CODE = 0x0001,
            DISASM_REGION_DATA = 0x0002,
            DISASM_REGION_INS_START = 0x0100,
            DISASM_REGION_BB_START = 0x0200,
            DISASM_REGION_FUNC_START = 0x0400
        };

        AddressMap() { }

        //void insert(ulong addr);
        //bool contains(ulong addr);
        //unsigned get_addr_type(ulong addr);
        //void set_addr_type(ulong addr, unsigned type);
        //void add_addr_flag(ulong addr, unsigned flag);
        //unsigned addr_type(ulong addr);

        //uint unmapped_count();
        //ulong get_unmapped(uint i);
        //void erase(ulong addr);
        //void erase_unmapped(ulong addr);

        private SortedList<ulong, DisasmRegion> addrmap;
        private List<ulong> unmapped;
        private SortedList<ulong, uint> unmapped_lookup;
    }
    public partial class DisasmSection
    {
        public DisasmSection() { section = null; }

        //void print_BBs(FILE*out);

        public Section section;
        public AddressMap addrmap;
        public List<BB> BBs;
        public List<DataRegion> data;

        //int nucleus_disasm(Binary* bin, std::list<DisasmSection>* disasm);


        public void print_BBs(TextWriter @out)
        {
            @out.WriteLine("<Section {0} {1} @0x{2:X16} (size %{3})>",
                    section.name, (section.type == Section.SectionType.SEC_TYPE_CODE) ? "C" : "D",
                    section.vma, section.size);
            @out.WriteLine();
            sort_BBs();
            foreach (var bb in BBs) {
                bb.print(@out);
            }
        }


        void
        sort_BBs()
        {
            BBs.Sort(BB.comparator);
        }
    }

    public partial class AddressMap
    {
        void
        insert(ulong addr)
        {
            if (!contains(addr)) {
                unmapped.Add(addr);
                unmapped_lookup[addr] = (uint)unmapped.Count - 1;
            }
        }


        bool
        contains(ulong addr)
        {
            return addrmap.ContainsKey(addr) || unmapped_lookup.ContainsKey(addr);
        }


        DisasmRegion
        get_addr_type(ulong addr)
        {
            Debug.Assert(contains(addr));
            if (!contains(addr)) {
                return AddressMap.DisasmRegion.DISASM_REGION_UNMAPPED;
            } else {
                return addrmap[addr];
            }
        }
        DisasmRegion addr_type(ulong addr) { return get_addr_type(addr); }


        void
        set_addr_type(ulong addr, AddressMap.DisasmRegion type)
        {
            Debug.Assert(contains(addr));
            if (contains(addr)) {
                if (type != AddressMap.DisasmRegion.DISASM_REGION_UNMAPPED) {
                    erase_unmapped(addr);
                }
                addrmap[addr] = type;
            }
        }


        void
        add_addr_flag(ulong addr, DisasmRegion flag)
        {
            Debug.Assert(contains(addr));
            if (contains(addr)) {
                if (flag != DisasmRegion.DISASM_REGION_UNMAPPED) {
                    erase_unmapped(addr);
                }
                addrmap[addr] |= flag;
            }
        }


        uint
        unmapped_count()
        {
            return (uint)unmapped.Count;
        }


        ulong
        get_unmapped(int i)
        {
            return unmapped[i];
        }


        void
        erase(ulong addr)
        {
            if (addrmap.ContainsKey(addr)) {
                addrmap.Remove(addr);
            }
            erase_unmapped(addr);
        }


        void erase_unmapped(ulong addr)
        {
            uint i;

            if (unmapped_lookup.ContainsKey(addr)) {
                if (unmapped_count() > 1) {
                    i = unmapped_lookup[addr];
                    unmapped[(int)i] = unmapped[unmapped.Count - 1];
                    unmapped_lookup[unmapped[unmapped.Count - 1]] = i;
                }
                unmapped_lookup.Remove(addr);
                unmapped.RemoveAt(unmapped.Count - 1);
            }
        }
    }

    public partial class Nucleus
    { 
/*******************************************************************************
 **                            Disassembly engine                             **
 ******************************************************************************/
        public static int 
init_disasm(Binary bin, List<DisasmSection> disasm)
{

  disasm.Clear();
  for(var i = 0; i < bin.sections.Count; i++) {
    var sec = bin.sections[i];
    if((sec.type != Section.SectionType.SEC_TYPE_CODE)
       && !(!options.only_code_sections && (sec.type == Section.SectionType.SEC_TYPE_DATA))) continue;

                var dis = new DisasmSection();
                disasm.Add(dis);

    dis.section = sec;
    for(vma = sec.vma; vma < (sec.vma+sec.size); vma++) {
      dis.addrmap.insert(vma);
    }
  }
  verbose(1, "disassembler initialized");

  return 0;  
}


static int
fini_disasm(Binary bin, List<DisasmSection> disasm)
{
  verbose(1, "disassembly complete");

  return 0;
}


static int
is_cs_nop_ins(cs_insn ins)
{
  switch(ins.id) {
  case X86_INS_NOP:
  case X86_INS_FNOP:
    return 1;
  default:
    return 0;
  }
}


static int
is_cs_semantic_nop_ins(cs_insn *ins)
{
  cs_x86 *x86;

  /* XXX: to make this truly platform-independent, we need some real
   * semantic analysis, but for now checking known cases is sufficient */

  x86 = &ins.detail.x86;
  switch(ins.id) {
  case X86_INS_MOV:
    /* mov reg,reg */
    if((x86.op_count == 2) 
       && (x86.operands[0].type == X86_OP_REG) 
       && (x86.operands[1].type == X86_OP_REG) 
       && (x86.operands[0].reg == x86.operands[1].reg)) {
      return 1;
    }
    return 0;
  case X86_INS_XCHG:
    /* xchg reg,reg */
    if((x86.op_count == 2) 
       && (x86.operands[0].type == X86_OP_REG) 
       && (x86.operands[1].type == X86_OP_REG) 
       && (x86.operands[0].reg == x86.operands[1].reg)) {
      return 1;
    }
    return 0;
  case X86_INS_LEA:
    /* lea    reg,[reg + 0x0] */
    if((x86.op_count == 2)
       && (x86.operands[0].type == X86_OP_REG)
       && (x86.operands[1].type == X86_OP_MEM)
       && (x86.operands[1].mem.segment == X86_REG_INVALID)
       && (x86.operands[1].mem.base == x86.operands[0].reg)
       && (x86.operands[1].mem.index == X86_REG_INVALID)
       /* mem.scale is irrelevant since index is not used */
       && (x86.operands[1].mem.disp == 0)) {
      return 1;
    }
    /* lea    reg,[reg + eiz*x + 0x0] */
    if((x86.op_count == 2)
       && (x86.operands[0].type == X86_OP_REG)
       && (x86.operands[1].type == X86_OP_MEM)
       && (x86.operands[1].mem.segment == X86_REG_INVALID)
       && (x86.operands[1].mem.base == x86.operands[0].reg)
       && (x86.operands[1].mem.index == X86_REG_EIZ)
       /* mem.scale is irrelevant since index is the zero-register */
       && (x86.operands[1].mem.disp == 0)) {
      return 1;
    }
    return 0;
  default:
    return 0;
  }
}


static int
is_cs_trap_ins(cs_insn *ins)
{
  switch(ins.id) {
  case X86_INS_INT3:
  case X86_INS_UD2:
    return 1;
  default:
    return 0;
  }
}


static int
is_cs_cflow_group(uint8_t g)
{
  return (g == CS_GRP_JUMP) || (g == CS_GRP_CALL) || (g == CS_GRP_RET) || (g == CS_GRP_IRET);
}


static int
is_cs_cflow_ins(cs_insn *ins)
{
  uint i;

  for(i = 0; i < ins.detail.groups_count; i++) {
    if(is_cs_cflow_group(ins.detail.groups[i])) {
      return 1;
    }
  }

  return 0;
}


static int
is_cs_call_ins(cs_insn *ins)
{
  switch(ins.id) {
  case X86_INS_CALL:
  case X86_INS_LCALL:
    return 1;
  default:
    return 0;
  }
}


static int
is_cs_ret_ins(cs_insn *ins)
{
  switch(ins.id) {
  case X86_INS_RET:
  case X86_INS_RETF:
    return 1;
  default:
    return 0;
  }
}


static int
is_cs_unconditional_jmp_ins(cs_insn *ins)
{
  switch(ins.id) {
  case X86_INS_JMP:
    return 1;
  default:
    return 0;
  }
}


static int
is_cs_conditional_cflow_ins(cs_insn *ins)
{
  switch(ins.id) {
  case X86_INS_JAE:
  case X86_INS_JA:
  case X86_INS_JBE:
  case X86_INS_JB:
  case X86_INS_JCXZ:
  case X86_INS_JECXZ:
  case X86_INS_JE:
  case X86_INS_JGE:
  case X86_INS_JG:
  case X86_INS_JLE:
  case X86_INS_JL:
  case X86_INS_JNE:
  case X86_INS_JNO:
  case X86_INS_JNP:
  case X86_INS_JNS:
  case X86_INS_JO:
  case X86_INS_JP:
  case X86_INS_JRCXZ:
  case X86_INS_JS:
    return 1;
  case X86_INS_JMP:
  default:
    return 0;
  }
}


static int
is_cs_privileged_ins(cs_insn *ins)
{
  switch(ins.id) {
  case X86_INS_HLT:
  case X86_INS_IN:
  case X86_INS_INSB:
  case X86_INS_INSW:
  case X86_INS_INSD:
  case X86_INS_OUT:
  case X86_INS_OUTSB:
  case X86_INS_OUTSW:
  case X86_INS_OUTSD:
  case X86_INS_RDMSR:
  case X86_INS_WRMSR:
  case X86_INS_RDPMC:
  case X86_INS_RDTSC:
  case X86_INS_LGDT:
  case X86_INS_LLDT:
  case X86_INS_LTR:
  case X86_INS_LMSW:
  case X86_INS_CLTS:
  case X86_INS_INVD:
  case X86_INS_INVLPG:
  case X86_INS_WBINVD:
    return 1;
  default:
    return 0;
  }
}


static uint8_t
cs_to_nucleus_op_type(x86_op_type op)
{
  switch(op) {
  case X86_OP_REG:
    return Operand::OP_TYPE_REG;
  case X86_OP_IMM:
    return Operand::OP_TYPE_IMM;
  case X86_OP_MEM:
    return Operand::OP_TYPE_MEM;
  case X86_OP_FP:
    return Operand::OP_TYPE_FP;
  case X86_OP_INVALID:
  default:
    return Operand::OP_TYPE_NONE;
  }
}


static int
nucleus_disasm_bb_x86(Binary bin, DisasmSection dis, BB bb)
{
  int init, ret, jmp, cflow, cond, call, nop, only_nop, priv, trap, ndisassembled;
  csh cs_dis;
  cs_mode cs_mode;
  cs_insn *cs_ins;
  cs_x86_op *cs_op;
  const uint8_t *pc;
  ulong pc_addr, offset;
  uint i, j, n;
  Instruction ins;
  Operand op;

  init   = 0;
  cs_ins = null;

  switch(bin.bits) {
  case 64:
    cs_mode = CS_MODE_64;
    break;
  case 32:
    cs_mode = CS_MODE_32;
    break;
  case 16:
    cs_mode = CS_MODE_16;
    break;
  default:
    print_err("unsupported bit width %u for architecture %s", bin.bits, bin.arch_str.c_str());
    goto fail;
  }

  if(cs_open(CS_ARCH_X86, cs_mode, &cs_dis) != CS_ERR_OK) {
    print_err("failed to initialize libcapstone");
    goto fail;
  }
  init = 1;
  cs_option(cs_dis, CS_OPT_DETAIL, CS_OPT_ON);
  cs_option(cs_dis, CS_OPT_SYNTAX, CS_OPT_SYNTAX_INTEL);

  cs_ins = cs_malloc(cs_dis);
  if(!cs_ins) {
    print_err("out of memory");
    goto fail;
  }

  offset = bb.start - dis.section.vma;
  if((bb.start < dis.section.vma) || (offset >= dis.section.size)) {
    print_err("basic block address points outside of section '%s'", dis.section.name.c_str());
    goto fail;
  }

  pc = dis.section.bytes + offset;
  n = dis.section.size - offset;
  pc_addr = bb.start;
  bb.end = bb.start;
  bb.section = dis.section;
  ndisassembled = 0;
  only_nop = 0;
  while(cs_disasm_iter(cs_dis, &pc, &n, &pc_addr, cs_ins)) {
    if(cs_ins.id == X86_INS_INVALID) {
      bb.invalid = 1;
      bb.end += 1;
      break;
    }
    if(!cs_ins.size) {
      break;
    }

    trap  = is_cs_trap_ins(cs_ins);
    nop   = is_cs_nop_ins(cs_ins) 
            /* Visual Studio sometimes places semantic nops at the function start */
            || (is_cs_semantic_nop_ins(cs_ins) && (bin.type != Binary::BIN_TYPE_PE))
            /* Visual Studio uses int3 for padding */
            || (trap && (bin.type == Binary::BIN_TYPE_PE));
    ret   = is_cs_ret_ins(cs_ins);
    jmp   = is_cs_unconditional_jmp_ins(cs_ins) || is_cs_conditional_cflow_ins(cs_ins);
    cond  = is_cs_conditional_cflow_ins(cs_ins);
    cflow = is_cs_cflow_ins(cs_ins);
    call  = is_cs_call_ins(cs_ins);
    priv  = is_cs_privileged_ins(cs_ins);

    if(!ndisassembled && nop) only_nop = 1; /* group nop instructions together */
    if(!only_nop && nop) break;
    if(only_nop && !nop) break;

    ndisassembled++;

    bb.end += cs_ins.size;
    bb.insns.push_back(Instruction());
    if(priv) {
      bb.privileged = true;
    }
    if(nop) {
      bb.padding = true;
    }
    if(trap) {
      bb.trap = true;
    }

    ins = &bb.insns.back();
    ins.start      = cs_ins.address;
    ins.size       = cs_ins.size;
    ins.addr_size  = cs_ins.detail.x86.addr_size;
    ins.mnem       = string(cs_ins.mnemonic);
    ins.op_str     = string(cs_ins.op_str);
    ins.privileged = priv;
    ins.trap       = trap;
    if(nop)   ins.flags |= Instruction::INS_FLAG_NOP;
    if(ret)   ins.flags |= Instruction::INS_FLAG_RET;
    if(jmp)   ins.flags |= Instruction::INS_FLAG_JMP;
    if(cond)  ins.flags |= Instruction::INS_FLAG_COND;
    if(cflow) ins.flags |= Instruction::INS_FLAG_CFLOW;
    if(call)  ins.flags |= Instruction::INS_FLAG_CALL;

    for(i = 0; i < cs_ins.detail.x86.op_count; i++) {
      cs_op = &cs_ins.detail.x86.operands[i];
      ins.operands.push_back(Operand());
      op = &ins.operands.back();
      op.type = cs_to_nucleus_op_type(cs_op.type);
      op.size = cs_op.size;
      if(op.type == Operand::OP_TYPE_IMM) {
        op.x86_value.imm = cs_op.imm;
      } else if(op.type == Operand::OP_TYPE_REG) {
        op.x86_value.reg = cs_op.reg;
        if(cflow) ins.flags |= Instruction::INS_FLAG_INDIRECT;
      } else if(op.type == Operand::OP_TYPE_FP) {
        op.x86_value.fp = cs_op.fp;
      } else if(op.type == Operand::OP_TYPE_MEM) {
        op.x86_value.mem.segment = cs_op.mem.segment;
        op.x86_value.mem.base    = cs_op.mem.base;
        op.x86_value.mem.index   = cs_op.mem.index;
        op.x86_value.mem.scale   = cs_op.mem.scale;
        op.x86_value.mem.disp    = cs_op.mem.disp;
        if(cflow) ins.flags |= Instruction::INS_FLAG_INDIRECT;
      }
    }

    for(i = 0; i < cs_ins.detail.groups_count; i++) {
      if(is_cs_cflow_group(cs_ins.detail.groups[i])) {
        for(j = 0; j < cs_ins.detail.x86.op_count; j++) {
          cs_op = &cs_ins.detail.x86.operands[j];
          if(cs_op.type == X86_OP_IMM) {
            ins.target = cs_op.imm;
          }
        }
      }
    }

    if(cflow) {
      /* end of basic block */
      break;
    }
  }

  if(!ndisassembled) {
    bb.invalid = 1;
    bb.end += 1; /* ensure forward progress */
  }

  ret = ndisassembled;
  goto cleanup;

fail:
  ret = -1;

cleanup:
  if(cs_ins) {
    cs_free(cs_ins, 1);
  }
  if(init) {
    cs_close(&cs_dis);
  }
  return ret;
}


static int
nucleus_disasm_bb(Binary *bin, DisasmSection *dis, BB *bb)
{
  switch(bin.arch) {
  case Binary::ARCH_X86:
    return nucleus_disasm_bb_x86(bin, dis, bb);
  default:
    print_err("disassembly for architecture %s is not supported", bin.arch_str.c_str());
    return -1;
  }
}


static int
nucleus_disasm_section(Binary bin, DisasmSection dis)
{
  int ret;
  uint i, n;
  ulong vma;
  double s;
  BB mutants;
  Queue<BB> Q;

  mutants = null;

  if((dis.section.type != Section.SectionType.SEC_TYPE_CODE) && options.only_code_sections) {
    print_warn("skipping non-code section '{0}'", dis.section.name);
    return 0;
  }

  verbose(2, "disassembling section '%s'", dis.section.name);

  Q.Enqueue(null);
  while(!Q.empty()) {
    n = bb_mutate(dis, Q.Peek(), out mutants);
    Q.Dequeue();
    for(i = 0; i < n; i++) {
      if(nucleus_disasm_bb(bin, dis, &mutants[i]) < 0) {
        goto fail;
      }
      if((s = bb_score(dis, &mutants[i])) < 0) {
        goto fail;
      }
    }
    if((n = bb_select(dis, mutants, n)) < 0) {
      goto fail;
    }
    for(i = 0; i < n; i++) {
      if(mutants[i].alive) {
        dis.addrmap.add_addr_flag(mutants[i].start, AddressMap::DISASM_REGION_BB_START);
        foreach (var ins in  mutants[i].insns) {
          dis.addrmap.add_addr_flag(ins.start, AddressMap::DISASM_REGION_INS_START);
        }
        for(vma = mutants[i].start; vma < mutants[i].end; vma++) {
          dis.addrmap.add_addr_flag(vma, AddressMap::DISASM_REGION_CODE);
        }
        dis.BBs.push_back(BB(mutants[i]));
        Q.push(&dis.BBs.back());
      } 
    }
  }

  ret = 0;
  goto cleanup;

fail:
  ret = -1;

cleanup:
  if(mutants) {
    delete[] mutants;
  }
  return ret;
}


int
nucleus_disasm(Binary *bin, std::list<DisasmSection> *disasm)
{
  int ret;

  if(init_disasm(bin, disasm) < 0) {
    goto fail;
  }

  foreach (var dis in  (*disasm)) {
    if(nucleus_disasm_section(bin, &dis) < 0) {
      goto fail;
    }
  }

  if(fini_disasm(bin, disasm) < 0) {
    goto fail;
  }

  ret = 0;
  goto cleanup;

fail:
  ret = -1;

cleanup:
  return ret;
}

