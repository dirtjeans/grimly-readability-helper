import Carbon.HIToolbox
import CoreGraphics

struct KeyCodeMapping {
    static func keyCode(for key: String) -> UInt16? {
        switch key.uppercased() {
        case "A": return UInt16(kVK_ANSI_A)
        case "B": return UInt16(kVK_ANSI_B)
        case "C": return UInt16(kVK_ANSI_C)
        case "D": return UInt16(kVK_ANSI_D)
        case "E": return UInt16(kVK_ANSI_E)
        case "F": return UInt16(kVK_ANSI_F)
        case "G": return UInt16(kVK_ANSI_G)
        case "H": return UInt16(kVK_ANSI_H)
        case "I": return UInt16(kVK_ANSI_I)
        case "J": return UInt16(kVK_ANSI_J)
        case "K": return UInt16(kVK_ANSI_K)
        case "L": return UInt16(kVK_ANSI_L)
        case "M": return UInt16(kVK_ANSI_M)
        case "N": return UInt16(kVK_ANSI_N)
        case "O": return UInt16(kVK_ANSI_O)
        case "P": return UInt16(kVK_ANSI_P)
        case "Q": return UInt16(kVK_ANSI_Q)
        case "R": return UInt16(kVK_ANSI_R)
        case "S": return UInt16(kVK_ANSI_S)
        case "T": return UInt16(kVK_ANSI_T)
        case "U": return UInt16(kVK_ANSI_U)
        case "V": return UInt16(kVK_ANSI_V)
        case "W": return UInt16(kVK_ANSI_W)
        case "X": return UInt16(kVK_ANSI_X)
        case "Y": return UInt16(kVK_ANSI_Y)
        case "Z": return UInt16(kVK_ANSI_Z)
        case "0": return UInt16(kVK_ANSI_0)
        case "1": return UInt16(kVK_ANSI_1)
        case "2": return UInt16(kVK_ANSI_2)
        case "3": return UInt16(kVK_ANSI_3)
        case "4": return UInt16(kVK_ANSI_4)
        case "5": return UInt16(kVK_ANSI_5)
        case "6": return UInt16(kVK_ANSI_6)
        case "7": return UInt16(kVK_ANSI_7)
        case "8": return UInt16(kVK_ANSI_8)
        case "9": return UInt16(kVK_ANSI_9)
        case "F1": return UInt16(kVK_F1)
        case "F2": return UInt16(kVK_F2)
        case "F3": return UInt16(kVK_F3)
        case "F4": return UInt16(kVK_F4)
        case "F5": return UInt16(kVK_F5)
        case "F6": return UInt16(kVK_F6)
        case "F7": return UInt16(kVK_F7)
        case "F8": return UInt16(kVK_F8)
        case "F9": return UInt16(kVK_F9)
        case "F10": return UInt16(kVK_F10)
        case "F11": return UInt16(kVK_F11)
        case "F12": return UInt16(kVK_F12)
        case "SPACE": return UInt16(kVK_Space)
        case "RETURN", "ENTER": return UInt16(kVK_Return)
        case "TAB": return UInt16(kVK_Tab)
        case "ESCAPE", "ESC": return UInt16(kVK_Escape)
        default: return nil
        }
    }

    static func modifierFlags(for modifierString: String) -> CGEventFlags {
        var flags: CGEventFlags = []
        let parts = modifierString.split(separator: "+").map { $0.trimmingCharacters(in: .whitespaces).lowercased() }

        for part in parts {
            switch part {
            case "cmd", "command": flags.insert(.maskCommand)
            case "option", "opt", "alt": flags.insert(.maskAlternate)
            case "shift": flags.insert(.maskShift)
            case "ctrl", "control": flags.insert(.maskControl)
            default: break
            }
        }

        return flags
    }
}
