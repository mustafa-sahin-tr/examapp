export interface StyleConfig {
  background: string;
  subBackground: string;
  ribbonColor: string;
  ribbonLeftColor: string;
  ribbonRightColor: string;  
  ribbonShadowColor: string;
  iconColor: string;
  size: string;
}

export const CARDSTYLES : {[key:string]: StyleConfig} = {
    default: {
        background: '#00a69d',
        subBackground: '#D6E1F1',
        ribbonColor: '#03817a',
        ribbonLeftColor: '#03817a',
        ribbonRightColor: '#03817a',
        ribbonShadowColor: '#02524e',
        iconColor: '#d6dbe1',
        size: '225px'
    },
    primary: {
        background: '#F5921D',
        subBackground: '#D6E1F1',
        ribbonColor: '#c17217',
        ribbonLeftColor: '#c17217',
        ribbonRightColor: '#c17217',
        ribbonShadowColor: '#77470f',
        iconColor: '#d6dbe1',
        size: '225px'
    },
    blue: {
        background: '#2BB5E6',
        subBackground: '#D6E1F1',
        ribbonColor: '#1f7e9e',
        ribbonLeftColor: '#1f7e9e',
        ribbonRightColor: '#1f7e9e',
        ribbonShadowColor: '#02524e',
        iconColor: '#d6dbe1',
        size: '225px'
    },
    green: {
        background: '#99cc33',
        subBackground: '#D6E1F1',
        ribbonColor: '#638421',
        ribbonLeftColor: '#638421',
        ribbonRightColor: '#638421',
        ribbonShadowColor: '#3b4f13',
        iconColor: '#d6dbe1',
        size: '225px'
    },
    orange: {
        background: '#d3932c',
        subBackground: '#D6E1F1',
        ribbonColor: '#89601e',
        ribbonLeftColor: '#89601e',
        ribbonRightColor: '#89601e',
        ribbonShadowColor: '#49330f',
        iconColor: '#d6dbe1',
        size: '225px'
    },
    red: {
        background: '#d8363e',
        subBackground: '#D6E1F1',
        ribbonColor: '#912228',
        ribbonLeftColor: '#912228',
        ribbonRightColor: '#912228',
        ribbonShadowColor: '#561418',
        iconColor: '#d6dbe1',
        size: '225px'
    },
    purple: {
        background: '#db3b85',
        subBackground: '#D6E1F1',
        ribbonColor: '#93275a',
        ribbonLeftColor: '#93275a',
        ribbonRightColor: '#93275a',
        ribbonShadowColor: '#511430',
        iconColor: '#d6dbe1',
        size: '225px'
    }
} 